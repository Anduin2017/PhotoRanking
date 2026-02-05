using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PhotosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ScoringService _scoringService;
    private readonly ILogger<PhotosController> _logger;
    private readonly VectorStorageService _vectorStorage;
    private readonly ImageAnalysisService _imageAnalysis;
    private readonly IConfiguration _configuration;

    public PhotosController(
        AppDbContext context, 
        ScoringService scoringService, 
        ILogger<PhotosController> logger,
        VectorStorageService vectorStorage,
        ImageAnalysisService imageAnalysis,
        IConfiguration configuration)
    {
        _context = context;
        _scoringService = scoringService;
        _logger = logger;
        _vectorStorage = vectorStorage;
        _imageAnalysis = imageAnalysis;
        _configuration = configuration;
    }

    /// <summary>
    /// 获取首页照片流（按浏览次数负相关、整体分正相关）
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<List<Photo>>> GetFeed(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int count = 0) // 保留count参数向后兼容
    {
        // 如果使用了count参数，使用旧逻辑
        if (count > 0)
        {
            pageSize = count;
            page = 1;
        }

        var photos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.OverallScore > 0)
            .ToListAsync();

        if (photos.Count == 0)
        {
            return Ok(new List<Photo>());
        }

        // 计算要选择的总数量
        int totalToSelect = page * pageSize;
        var limit = Math.Min(totalToSelect, photos.Count);

        // 加权：整体分高、浏览次数低的照片权重更高
        var selectedPhotos = new List<Photo>();
        for (int i = 0; i < limit; i++)
        {
            var photo = _scoringService.WeightedRandomSelect(photos, p =>
            {
                var scoreWeight = Math.Pow(p.OverallScore, 2); // 分数平方作为权重
                var viewPenalty = 1.0 / (p.ViewCount + 1); // 浏览次数越多，权重越低
                return scoreWeight * viewPenalty;
            });

            selectedPhotos.Add(photo);
            photos.Remove(photo); // 避免重复
        }

        // 只返回当前页的照片
        var currentPagePhotos = selectedPhotos
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(currentPagePhotos);
    }

    /// <summary>
    /// 获取探索页面的照片（支持模式和分页）
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<List<Photo>>> GetDiscover(
        [FromQuery] string mode = "waiting",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] double? minScore = null)
    {
        List<Photo> candidates;

        // 根据模式选择候选照片
        switch (mode.ToLower())
        {
            case "waiting": // 待打分：纯随机
                candidates = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => p.IndependentScore == null)
                    .OrderBy(p => EF.Functions.Random())
                    .Take(500)
                    .ToListAsync();
                break;

            case "consolidate": // 巩固：尽可能将已知率较高的相册里还未评分的照片进行评分
                var topAlbums = await _context.Albums
                    .Where(a => a.KnownRate < 1)
                    .OrderByDescending(a => a.KnownRate)
                    .Take(35)
                    .ToListAsync();

                var albumIds = topAlbums.Select(a => a.AlbumId).ToList();

                candidates = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => albumIds.Contains(p.AlbumId) && p.IndependentScore == null)
                    .ToListAsync();

                if (candidates.Count == 0)
                {
                    candidates = await _context.Photos
                        .Include(p => p.Album)
                        .Where(p => p.IndependentScore == null)
                        .OrderByDescending(p => p.Album.KnownRate)
                        .Take(500)
                        .ToListAsync();
                }
                break;

            case "enjoy": // 享受：只有设置的分数综合分以上的照片
                var actualMinScore = minScore ?? 3.0;
                candidates = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => p.OverallScore >= actualMinScore)
                    .ToListAsync();
                break;

            case "featured": // 特选：按独立分随机刷
                var targetScore = minScore ?? 5.0;
                candidates = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => p.IndependentScore >= targetScore - 0.0001 && p.IndependentScore <= targetScore + 0.0001)
                    .ToListAsync();
                break;

            default:
                return BadRequest("Invalid mode");
        }

        if (candidates.Count == 0)
        {
            return Ok(new List<Photo>());
        }

        // 定义权重选择器
        double WeightSelector(Photo p) => mode.ToLower() switch
        {
            "waiting" => 100 - p.Knownness + 1,
            "consolidate" => p.Album.KnownRate * 100 + 1,
            "enjoy" => Math.Pow(p.OverallScore + 1, 2) / (p.ViewCount + 1),
            "featured" => 1.0 / (p.ViewCount + 1), // 特选模式下：浏览次数越少权重越高
            _ => 1.0
        };

        // 使用加权随机选择
        var totalToSelect = page * pageSize;
        var selectedPhotos = new List<Photo>();
        var limit = Math.Min(totalToSelect, candidates.Count);
        for (int i = 0; i < limit; i++)
        {
            var photo = _scoringService.WeightedRandomSelect(candidates, WeightSelector);
            selectedPhotos.Add(photo);
            candidates.Remove(photo);
        }

        // 只返回当前页的照片
        var currentPagePhotos = selectedPhotos
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(currentPagePhotos);
    }

    /// <summary>
    /// 获取照片详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<object>> GetPhoto(int id)
    {
        var photo = await _context.Photos
            .Include(p => p.Album)
            .Include(p => p.RatingLogs)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (photo == null)
        {
            return NotFound();
        }

        // 计算历史独立分均分
        var avgIndependentScore = photo.RatingLogs.Any()
            ? photo.RatingLogs.Average(r => r.Score)
            : (double?)null;

        return Ok(new
        {
            photo.Id,
            photo.FilePath,
            photo.AlbumId,
            photo.IndependentScore,
            photo.OverallScore,
            photo.Knownness,
            photo.RatingCount,
            photo.IsFixed,
            photo.ViewCount,
            photo.LastRatedAt,
            photo.Album,
            AvgIndependentScore = avgIndependentScore,
            RatingHistory = photo.RatingLogs.OrderByDescending(r => r.RatedAt).Take(10)
        });
    }

    /// <summary>
    /// 为照片打分
    /// </summary>
    [HttpPost("{id}/rate")]
    public async Task<ActionResult<Photo>> RatePhoto(int id, [FromBody] RateRequest request)
    {
        try
        {
            var photo = await _scoringService.RatePhotoAsync(id, request.Score);
            return Ok(photo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rating photo {PhotoId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 增加照片浏览次数
    /// </summary>
    [HttpPost("{id}/view")]
    public async Task<ActionResult> IncrementView(int id)
    {
        await _scoringService.IncrementViewCountAsync(id);
        return Ok();
    }

    /// <summary>
    /// 获取同相册的下一张照片（用于照片浏览器）
    /// </summary>
    [HttpGet("{id}/next-in-album")]
    public async Task<ActionResult<Photo>> GetNextInAlbum(int id)
    {
        var currentPhoto = await _context.Photos.FindAsync(id);
        if (currentPhoto == null)
        {
            return NotFound();
        }

        var albumPhotos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.AlbumId == currentPhoto.AlbumId)
            .ToListAsync();

        if (albumPhotos.Count == 0)
        {
            return NotFound();
        }

        // 按已知性正相关、整体分正相关选择
        var nextPhoto = _scoringService.WeightedRandomSelect(albumPhotos, p =>
        {
            return p.Knownness * Math.Pow(p.OverallScore, 2);
        });

        return Ok(nextPhoto);
    }

    /// <summary>
    /// 获取与指定照片相似的照片
    /// </summary>
    [HttpGet("{id}/similar")]
    public async Task<ActionResult<List<Photo>>> GetSimilar(int id, [FromQuery] int take = 10)
    {
        var targetPhoto = await _context.Photos.FindAsync(id);
        if (targetPhoto == null)
        {
            return NotFound("Target photo not found.");
        }

        // Lazy generation if vector is missing
        if (targetPhoto.FeatureVector == null)
        {
            var photoRootPath = _configuration["PhotoRootPath"];
            if (string.IsNullOrEmpty(photoRootPath))
            {
                return StatusCode(500, "PhotoRootPath not configured.");
            }

            var fullPath = Path.Combine(photoRootPath, targetPhoto.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound("Photo file not found on disk.");
            }

            try
            {
                var vector = _imageAnalysis.GenerateVector(fullPath);
                if (vector != null)
                {
                    targetPhoto.FeatureVector = vector;
                    _vectorStorage.UpdateOrAdd(targetPhoto.Id, vector);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    return BadRequest("Failed to generate feature vector for this image.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating vector for photo {Id}", id);
                return StatusCode(500, "Internal error during vector generation.");
            }
        }

        var targetVector = ImageAnalysisService.ByteArrayToFloatArray(targetPhoto.FeatureVector);
        var similarIds = _vectorStorage.Search(targetVector, take + 1); // +1 because it includes itself
        
        // Filter out itself and ensure we have enough
        similarIds = similarIds.Where(pid => pid != id).Take(take).ToList();

        var similarPhotos = await _context.Photos
            .Where(p => similarIds.Contains(p.Id))
            .Include(p => p.Album)
            .ToListAsync();
            
        // Re-order in memory because SQL 'IN' clause doesn't guarantee order
        similarPhotos = similarPhotos
            .OrderBy(p => similarIds.IndexOf(p.Id))
            .ToList();

        return Ok(similarPhotos);
    }

    /// <summary>
    /// 上传图片搜索相似内容
    /// </summary>
    [HttpPost("search-by-image")]
    public async Task<ActionResult<List<Photo>>> SearchByImage(IFormFile? file, [FromQuery] int take = 10)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var tempPath = Path.GetTempFileName();
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var service = HttpContext.RequestServices.GetRequiredService<ImageAnalysisService>();
            var vectorBytes = service.GenerateVector(tempPath);
            if (vectorBytes == null)
                return BadRequest("Could not generate vector for uploaded image.");
                
            var vector = ImageAnalysisService.ByteArrayToFloatArray(vectorBytes);
            var similarIds = _vectorStorage.Search(vector, take);

            var similarPhotos = await _context.Photos
                .Where(p => similarIds.Contains(p.Id))
                .Include(p => p.Album)
                .ToListAsync();

            // Re-order
            similarPhotos = similarPhotos
                .OrderBy(p => similarIds.IndexOf(p.Id))
                .ToList();

            return Ok(similarPhotos);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 获取高级统计页面数据
    /// </summary>
    [HttpGet("stats/top")]
    public async Task<ActionResult<object>> GetTopStats()
    {
        var topAlbumsByKnownRate = await _context.Albums
            .OrderByDescending(a => a.KnownRate)
            .Take(10)
            .ToListAsync();

        var topAlbumsByScore = await _context.Albums
            .OrderByDescending(a => a.AlbumScore)
            .Take(10)
            .ToListAsync();

        var topPhotosByKnownness = await _context.Photos
            .Include(p => p.Album)
            .OrderByDescending(p => p.Knownness)
            .Take(20)
            .ToListAsync();

        var topPhotosByScore = await _context.Photos
            .Include(p => p.Album)
            .OrderByDescending(p => p.OverallScore)
            .Take(20)
            .ToListAsync();

        // 为每个相册添加代表性照片（独立分最高的照片）
        var albumsWithThumbnails = new List<dynamic>();

        foreach (var album in topAlbumsByKnownRate)
        {
            var topPhoto = await _context.Photos
                .Where(p => p.AlbumId == album.AlbumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.OverallScore)
                .FirstOrDefaultAsync();

            if (topPhoto == null)
            {
                topPhoto = await _context.Photos
                    .Where(p => p.AlbumId == album.AlbumId)
                    .FirstOrDefaultAsync();
            }

            albumsWithThumbnails.Add(new
            {
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }

        var albumsByScoreWithThumbnails = new List<dynamic>();

        foreach (var album in topAlbumsByScore)
        {
            var topPhoto = await _context.Photos
                .Where(p => p.AlbumId == album.AlbumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.OverallScore)
                .FirstOrDefaultAsync();

            if (topPhoto == null)
            {
                topPhoto = await _context.Photos
                    .Where(p => p.AlbumId == album.AlbumId)
                    .FirstOrDefaultAsync();
            }

            albumsByScoreWithThumbnails.Add(new
            {
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }

        return Ok(new
        {
            TopAlbumsByKnownRate = albumsWithThumbnails,
            TopAlbumsByScore = albumsByScoreWithThumbnails,
            TopPhotosByKnownness = topPhotosByKnownness,
            TopPhotosByScore = topPhotosByScore
        });
    }

    /// <summary>
    /// 获取整体分最高的照片（分页）
    /// </summary>
    [HttpGet("top-by-score")]
    public async Task<ActionResult<List<Photo>>> GetTopByScore([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .OrderByDescending(p => p.OverallScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }

    /// <summary>
    /// 获取已知性最高的照片（分页）
    /// </summary>
    [HttpGet("top-by-knownness")]
    public async Task<ActionResult<List<Photo>>> GetTopByKnownness([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .OrderByDescending(p => p.Knownness)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }
}

public class RateRequest
{
    public int Score { get; set; }
}