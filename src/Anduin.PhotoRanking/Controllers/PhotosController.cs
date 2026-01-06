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

    public PhotosController(AppDbContext context, ScoringService scoringService, ILogger<PhotosController> logger)
    {
        _context = context;
        _scoringService = scoringService;
        _logger = logger;
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

        // 加权：整体分高、浏览次数低的照片权重更高
        var selectedPhotos = new List<Photo>();
        for (int i = 0; i < Math.Min(totalToSelect, photos.Count); i++)
        {
            var photo = _scoringService.WeightedRandomSelect(photos, p =>
            {
                var scoreWeight = Math.Pow(p.OverallScore, 2); // 分数平方作为权重
                var viewPenalty = 1.0 / (p.ViewCount + 1); // 浏览次数越多，权重越低
                return scoreWeight * viewPenalty;
            });

            selectedPhotos.Add(photo);
            photos.Remove(photo); // 避免重复

            if (photos.Count == 0) break;
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
        [FromQuery] int pageSize = 30)
    {
        var allPhotos = await _context.Photos
            .Include(p => p.Album)
            .ToListAsync();

        if (allPhotos.Count == 0)
        {
            return Ok(new List<Photo>());
        }

        List<Photo> candidates;

        // 根据模式选择候选照片
        switch (mode.ToLower())
        {
            case "waiting": // 待打分：已知性最低
                candidates = allPhotos
                    .OrderBy(p => p.Knownness)
                    .ThenBy(_ => Guid.NewGuid())
                    .Take(Math.Min(500, allPhotos.Count))
                    .ToList();
                break;

            case "consolidate": // 巩固：已知性中等或整体分较高
                candidates = allPhotos
                    .Where(p =>
                        (p.Knownness > 10 && p.Knownness < 90) ||
                        (p.OverallScore > 2.8)
                    )
                    .ToList();

                if (candidates.Count == 0)
                {
                    candidates = allPhotos;
                }
                break;

            case "enjoy": // 享受：整体分越高越好
                // 只显示被评分过的照片（排除默认2.5分）
                // 第1优先级：高分照片（>3.5且被评分过）
                candidates = allPhotos
                    .Where(p => p.OverallScore > 3.5 && p.RatingCount > 0)
                    .ToList();

                // 第2优先级：中高分照片（>3.0且被评分过）
                if (candidates.Count < 100)
                {
                    candidates = allPhotos
                        .Where(p => p.OverallScore > 3.0 && p.RatingCount > 0)
                        .ToList();
                }

                // 第3优先级：所有被评分过的照片，按分数降序
                if (candidates.Count < 50)
                {
                    candidates = allPhotos
                        .Where(p => p.RatingCount > 0)
                        .OrderByDescending(p => p.OverallScore)
                        .ToList();
                }

                // 如果用户还没评分过任何照片，显示提示
                if (candidates.Count == 0)
                {
                    // 返回空列表，前端会显示"暂无照片"
                    return Ok(new List<Photo>());
                }
                break;

            default:
                return BadRequest("Invalid mode");
        }

        // 使用加权随机选择
        var selectedPhotos = new List<Photo>();
        var skip = (page - 1) * pageSize;

        // 如果候选照片不够，直接返回
        if (skip >= candidates.Count)
        {
            return Ok(new List<Photo>());
        }

        for (int i = 0; i < Math.Min(pageSize, candidates.Count - skip); i++)
        {
            Photo photo;

            if (mode.ToLower() == "waiting")
            {
                // 待打分：已知性越低权重越高
                photo = _scoringService.WeightedRandomSelect(candidates, p => 100 - p.Knownness + 1);
            }
            else if (mode.ToLower() == "consolidate")
            {
                // 巩固：已知性低 + 整体分高
                photo = _scoringService.WeightedRandomSelect(candidates, p =>
                {
                    var knownnessPenalty = Math.Max(1, 100 - p.Knownness);
                    var scoreBoost = Math.Max(1, Math.Pow(p.OverallScore, 2));
                    return knownnessPenalty * scoreBoost;
                });
            }
            else // enjoy
            {
                // 享受：整体分的3次方
                photo = _scoringService.WeightedRandomSelect(candidates, p => Math.Pow(Math.Max(0.1, p.OverallScore), 3));
            }

            selectedPhotos.Add(photo);
            candidates.Remove(photo);

            if (candidates.Count == 0) break;
        }

        return Ok(selectedPhotos);
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
