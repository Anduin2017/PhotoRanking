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
    private readonly PersonalizedPredictionService _predictionService;
    private readonly ILogger<PhotosController> _logger;
    private readonly ImageAnalysisService _imageAnalysis;

    private readonly IConfiguration _configuration;

    public PhotosController(
        AppDbContext context, 
        ScoringService scoringService, 
        PersonalizedPredictionService predictionService,
        ILogger<PhotosController> logger,
        ImageAnalysisService imageAnalysis,
        IConfiguration configuration)
    {
        _context = context;
        _scoringService = scoringService;
        _predictionService = predictionService;
        _logger = logger;
        _imageAnalysis = imageAnalysis;
        _configuration = configuration;
    }

    /// <summary>
    /// 获取首页 For You 照片流。只返回未评分照片，按个人预测分稳定降序。
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<List<Photo>>> GetFeed(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] int? take = null,
        [FromQuery] double? beforeScore = null,
        [FromQuery] int? beforeId = null)
    {
        var pageSize = Math.Clamp(take ?? size, 1, 100);
        page = Math.Max(1, page);

        var query = _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore == null);

        if (beforeScore.HasValue && beforeId.HasValue)
        {
            query = query.Where(p =>
                p.EstimatedScore == null ||
                p.EstimatedScore < beforeScore.Value ||
                (p.EstimatedScore == beforeScore.Value && p.Id > beforeId.Value));
        }
        else if (beforeId.HasValue)
        {
            // Null predictions sort last. This keeps a new installation pageable while
            // its first model is still training and filling the prediction cache.
            query = query.Where(p => p.EstimatedScore == null && p.Id > beforeId.Value);
        }

        var orderedQuery = query
            .OrderByDescending(p => p.EstimatedScore)
            .ThenBy(p => p.Id);

        var photos = await (beforeId.HasValue
                ? orderedQuery
                : orderedQuery.Skip((page - 1) * pageSize))
            .Take(pageSize)
            .ToListAsync();

        return Ok(photos);
    }

    /// <summary>
    /// 获取探索页面的照片（支持模式和分页）
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<List<Photo>>> GetDiscover(
        [FromQuery] string mode = "waiting",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] double? minScore = null,
        [FromQuery] double? maxScore = null,
        [FromQuery] int? shuffleSeed = null,
        [FromQuery] string sort = "random")
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedMode = mode.ToLowerInvariant();
        var stableSeed = (int)(Math.Abs((long)(shuffleSeed ?? Random.Shared.Next())) % int.MaxValue);

        // Random browsing is deterministic within one client session, so infinite scroll
        // does not repeat the same photos on adjacent pages.
        if (normalizedMode is "waiting" or "consolidate")
        {
            var randomPage = await _context.Photos
                .Include(p => p.Album)
                .Where(p => p.IndependentScore == null)
                .OrderBy(p => (((long)p.Id * 1103515245L) + stableSeed) % int.MaxValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return Ok(randomPage);
        }

        if (normalizedMode == "work")
        {
            var uncertainCandidates = await _context.Photos
                .Include(p => p.Album)
                .Where(p => p.IndependentScore == null && p.PredictionNovelty != null)
                .OrderByDescending(p => p.PredictionNovelty)
                .ThenByDescending(p => p.PredictionUncertainty)
                .Take(10_000)
                .ToListAsync();

            if (uncertainCandidates.Count == 0)
            {
                uncertainCandidates = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => p.IndependentScore == null && p.PredictionUncertainty != null)
                    .OrderByDescending(p => p.PredictionUncertainty)
                    .Take(10_000)
                    .ToListAsync();
            }

            if (uncertainCandidates.Count == 0)
            {
                var fallback = await _context.Photos
                    .Include(p => p.Album)
                    .Where(p => p.IndependentScore == null)
                    .OrderByDescending(p => p.EstimatedScore)
                    .ThenBy(p => p.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                return Ok(fallback);
            }

            // Visual novelty finds regions poorly covered by existing manual anchors;
            // ensemble disagreement is the fallback/tie-breaker. The first pass takes at
            // most one photo per album, and repeated views lower priority.
            var priorityOrdered = uncertainCandidates
                .OrderByDescending(p => (p.PredictionNovelty ?? p.PredictionUncertainty ?? 0) /
                                        Math.Sqrt(p.ViewCount + 1.0))
                .ThenByDescending(p => p.PredictionUncertainty)
                .ThenBy(p => StableShuffleKey(p.Id, stableSeed))
                .ToList();
            var diverseFirstPass = priorityOrdered
                .GroupBy(p => p.AlbumId)
                .Select(group => group.First())
                .OrderByDescending(p => (p.PredictionNovelty ?? p.PredictionUncertainty ?? 0) /
                                        Math.Sqrt(p.ViewCount + 1.0))
                .ThenByDescending(p => p.PredictionUncertainty)
                .ThenBy(p => StableShuffleKey(p.Id, stableSeed))
                .ToList();
            var firstPassIds = diverseFirstPass.Select(p => p.Id).ToHashSet();
            var workQueue = diverseFirstPass
                .Concat(priorityOrdered.Where(p => !firstPassIds.Contains(p.Id)))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Ok(workQueue);
        }

        List<Photo> candidates;
        if (normalizedMode == "enjoy")
        {
            var lower = Math.Clamp(minScore ?? 4.0, 0, 6);
            var upper = Math.Clamp(maxScore ?? 6.0, lower, 6);
            candidates = await _context.Photos
                .Include(p => p.Album)
                .Where(p => p.IndependentScore >= lower && p.IndependentScore <= upper)
                .ToListAsync();
        }
        else if (normalizedMode == "featured")
        {
            var targetScore = Math.Clamp(minScore ?? 5.0, 0, 6);
            candidates = await _context.Photos
                .Include(p => p.Album)
                .Where(p => p.IndependentScore >= targetScore - 0.0001 &&
                            p.IndependentScore <= targetScore + 0.0001)
                .ToListAsync();
        }
        else
        {
            return BadRequest("Invalid mode");
        }

        IEnumerable<Photo> ordered = sort.ToLowerInvariant() switch
        {
            "manualdesc" or "overalldesc" => candidates.OrderByDescending(p => p.IndependentScore).ThenBy(p => p.Id),
            "manualasc" or "overallasc" => candidates.OrderBy(p => p.IndependentScore).ThenBy(p => p.Id),
            "predicteddesc" or "estimateddesc" => candidates.OrderByDescending(p => p.EstimatedScore ?? -1).ThenBy(p => p.Id),
            "predictedasc" or "estimatedasc" => candidates.OrderBy(p => p.EstimatedScore ?? -1).ThenBy(p => p.Id),
            _ => candidates.OrderBy(p => StableShuffleKey(p.Id, stableSeed))
        };

        return Ok(ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList());
    }

    /// <summary>
    /// 获取照片详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<object>> GetPhoto(int id)
    {
        var photo = await _context.Photos
            .Include(p => p.Album)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (photo == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            photo.Id,
            photo.FilePath,
            photo.AlbumId,
            photo.IndependentScore,
            photo.ManualScore,
            photo.EstimatedScore,
            photo.PredictedScore,
            photo.EstimatedScoreModelVersion,
            photo.PredictionUncertainty,
            photo.PredictionNovelty,
            photo.DisplayScore,
            // Legacy fields are returned for old clients but no longer drive behavior.
            photo.OverallScore,
            photo.RatingCount,
            photo.ViewCount,
            photo.LastRatedAt,
            photo.Album
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

        // 同相册浏览只依据最终人工分或预测分，并降低重复浏览权重。
        var nextPhoto = _scoringService.WeightedRandomSelect(albumPhotos, p =>
        {
            return Math.Pow((p.IndependentScore ?? p.EstimatedScore ?? 0) + 1, 2) / (p.ViewCount + 1);
        });

        return Ok(nextPhoto);
    }

    /// <summary>
    /// 获取与指定照片相似的照片
    /// </summary>
    [HttpGet("{id}/similar")]
    public async Task<ActionResult<List<Photo>>> GetSimilar(int id, [FromQuery] int skip = 0, [FromQuery] int take = 10)
    {
        var targetPhoto = await _context.Photos.FindAsync(id);
        if (targetPhoto == null)
        {
            return NotFound("Target photo not found.");
        }

        var vector = await _scoringService.EnsureFeatureVectorPublic(targetPhoto);
        if (vector == null)
        {
            return BadRequest("Could not generate feature vector for this image.");
        }

        var similarPhotos = await _context.Photos
            .FromSqlInterpolated($@"
                SELECT * FROM Photos 
                WHERE Id != {id} AND FeatureVector IS NOT NULL
                ORDER BY VectorDistance(FeatureVector, {vector}) ASC
                LIMIT {take} OFFSET {skip}")
            .Include(p => p.Album)
            .ToListAsync();

        var targetVector = ImageAnalysisService.ByteArrayToFloatArray(vector);
        foreach (var photo in similarPhotos)
        {
            if (photo.FeatureVector != null)
            {
                var photoVector = ImageAnalysisService.ByteArrayToFloatArray(photo.FeatureVector);
                photo.Similarity = Math.Max(0, ImageAnalysisService.CalculateCosineSimilarity(targetVector, photoVector));
            }
        }

        return Ok(similarPhotos);
    }

    /// <summary>
    /// 使用当前个人化模型预测最终人工分。
    /// </summary>
    [HttpGet("{id}/guess-score")]
    public async Task<ActionResult<object>> GuessScore(int id)
    {
        var targetPhoto = await _context.Photos.FindAsync(id);
        if (targetPhoto == null)
        {
            return NotFound("Target photo not found.");
        }

        var vector = await _scoringService.EnsureFeatureVectorPublic(targetPhoto);
        if (vector == null)
        {
            return BadRequest("Could not ensure feature vector for this photo.");
        }

        var result = await _predictionService.PredictAsync(vector, HttpContext.RequestAborted);
        if (result == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "个人化预测模型尚未完成训练，请稍后再试。"
            });
        }

        return Ok(new
        {
            predictedScore = result.Score,
            uncertainty = result.Uncertainty,
            novelty = result.Novelty,
            modelVersion = result.ModelVersion,
            votes = new Dictionary<int, double>() // 旧客户端兼容
        });
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

            var vectorBytes = _imageAnalysis.GenerateVector(tempPath);
            if (vectorBytes == null)
                return BadRequest("Could not generate vector for uploaded image.");
                
            var similarPhotos = await _context.Photos
                .FromSqlInterpolated($@"
                    SELECT * FROM Photos 
                    WHERE FeatureVector IS NOT NULL
                    ORDER BY VectorDistance(FeatureVector, {vectorBytes}) ASC
                    LIMIT {take}")
                .Include(p => p.Album)
                .ToListAsync();

            var targetVector = ImageAnalysisService.ByteArrayToFloatArray(vectorBytes);
            foreach (var photo in similarPhotos)
            {
                if (photo.FeatureVector != null)
                {
                    var photoVector = ImageAnalysisService.ByteArrayToFloatArray(photo.FeatureVector);
                    photo.Similarity = Math.Max(0, ImageAnalysisService.CalculateCosineSimilarity(targetVector, photoVector));
                }
            }

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
        var topAlbumsByRatedRate = await _context.Albums
            .OrderByDescending(a => a.KnownRate)
            .Take(10)
            .ToListAsync();

        var topAlbumsByScore = await _context.Albums
            .OrderByDescending(a => a.AlbumScore)
            .Take(10)
            .ToListAsync();

        var topPredictedUnratedPhotos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore == null && p.EstimatedScore != null)
            .OrderByDescending(p => p.EstimatedScore)
            .Take(20)
            .ToListAsync();

        var topManualPhotos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore != null)
            .OrderByDescending(p => p.IndependentScore)
            .Take(20)
            .ToListAsync();

        var ratingHistory = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.LastRatedAt != null)
            .OrderByDescending(p => p.LastRatedAt)
            .Take(20)
            .ToListAsync();

        // 为每个相册添加代表性照片（独立分最高的照片）
        var albumsWithThumbnails = new List<dynamic>();

        foreach (var album in topAlbumsByRatedRate)
        {
            var topPhoto = await _context.Photos
                .Where(p => p.AlbumId == album.AlbumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.EstimatedScore)
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
                album.RatedRate,
                album.RatedPhotoCount,
                album.AverageManualScore,
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
                .ThenByDescending(p => p.EstimatedScore)
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
                album.RatedRate,
                album.RatedPhotoCount,
                album.AverageManualScore,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }

        return Ok(new
        {
            TopAlbumsByRatedRate = albumsWithThumbnails,
            TopAlbumsByKnownRate = albumsWithThumbnails, // Legacy API alias
            TopAlbumsByScore = albumsByScoreWithThumbnails,
            TopPredictedUnratedPhotos = topPredictedUnratedPhotos,
            TopPhotosByKnownness = topPredictedUnratedPhotos, // Legacy API alias
            TopManualPhotos = topManualPhotos,
            TopPhotosByScore = topManualPhotos, // Legacy API alias
            RatingHistory = ratingHistory
        });
    }

    /// <summary>
    /// 获取最终人工分最高的照片（分页）
    /// </summary>
    [HttpGet("top-by-score")]
    public async Task<ActionResult<List<Photo>>> GetTopByScore([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore != null)
            .OrderByDescending(p => p.IndependentScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }

    /// <summary>
    /// 旧接口兼容：已知性已删除，现在返回 AI 预测最高的未评分照片。
    /// </summary>
    [HttpGet("top-by-knownness")]
    public async Task<ActionResult<List<Photo>>> GetTopByKnownness([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore == null && p.EstimatedScore != null)
            .OrderByDescending(p => p.EstimatedScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }

    [HttpGet("top-predicted")]
    public async Task<ActionResult<List<Photo>>> GetTopPredicted([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore == null && p.EstimatedScore != null)
            .OrderByDescending(p => p.EstimatedScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }

    /// <summary>
    /// 获取打分历史（按最后打分时间逆序，照片去重）
    /// </summary>
    [HttpGet("rating-history")]
    public async Task<ActionResult<List<Photo>>> GetRatingHistory(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        var photos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.LastRatedAt != null)
            .OrderByDescending(p => p.LastRatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(photos);
    }

    /// <summary>
    /// 删除单张照片（从文件系统和数据库中删除）
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePhoto(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
        {
            return NotFound();
        }

        var photoRootPath = _configuration["PhotoRootPath"];

        if (!string.IsNullOrEmpty(photoRootPath) && !photo.FilePath.Contains("..") && !Path.IsPathRooted(photo.FilePath))
        {
            var fullPath = Path.Combine(photoRootPath, photo.FilePath);
            if (System.IO.File.Exists(fullPath))
            {
                try
                {
                    System.IO.File.Delete(fullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete file {FullPath}", fullPath);
                }
            }
        }

        var albumId = photo.AlbumId;
        _context.Photos.Remove(photo);
        await _context.SaveChangesAsync();

        await _scoringService.UpdateAlbumScoreAsync(albumId);

        return Ok(new { deleted = true, id });
    }

    /// <summary>
    /// 批量删除选定的照片
    /// </summary>
    [HttpPost("bulk-delete")]
    public async Task<ActionResult> BulkDelete([FromBody] List<int> photoIds)
    {
        if (photoIds.Count == 0)
            return BadRequest("No photos selected for deletion.");

        var photosToDelete = await _context.Photos.Where(p => photoIds.Contains(p.Id)).ToListAsync();
        var photoRootPath = _configuration["PhotoRootPath"];

        foreach (var photo in photosToDelete)
        {
            if (!string.IsNullOrEmpty(photoRootPath) && !photo.FilePath.Contains("..") && !Path.IsPathRooted(photo.FilePath))
            {
                var fullPath = Path.Combine(photoRootPath, photo.FilePath);
                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file {FullPath}", fullPath);
                    }
                }
            }

            _context.Photos.Remove(photo);
        }

        // Group photos by album to re-calculate stats for affected albums
        var affectedAlbumIds = photosToDelete.Select(p => p.AlbumId).Distinct().ToList();
        await _context.SaveChangesAsync();

        foreach (var albumId in affectedAlbumIds)
        {
            await _scoringService.UpdateAlbumScoreAsync(albumId);
        }

        return Ok(new { deletedCount = photosToDelete.Count });
    }

    private static uint StableShuffleKey(int photoId, int seed)
    {
        var value = unchecked((uint)photoId ^ (uint)seed);
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return value;
    }
}

public class RateRequest
{
    public int Score { get; set; }
}
