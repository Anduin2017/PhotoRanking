using Aiursoft.Canon;
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
    private readonly ImageAnalysisService _imageAnalysis;
    private readonly IConfiguration _configuration;
    private readonly CanonPool _canonPool;

    public PhotosController(
        AppDbContext context, 
        ScoringService scoringService, 
        ILogger<PhotosController> logger,
        ImageAnalysisService imageAnalysis,
        IConfiguration configuration,
        CanonPool canonPool)
    {
        _context = context;
        _scoringService = scoringService;
        _logger = logger;
        _imageAnalysis = imageAnalysis;
        _configuration = configuration;
        _canonPool = canonPool;
    }

    /// <summary>
    /// 获取首页照片流（基于未评分照片的并行质量预测推荐）
    /// </summary>
    [HttpGet("feed")]
    public async Task<ActionResult<List<Photo>>> GetFeed(
        [FromQuery] int size = 20,
        [FromQuery] int pool = 200)
    {
        // 1. 从全库随机抽取未评分照片
        var unratedPhotos = await _context.Photos
            .Include(p => p.Album)
            .Where(p => p.IndependentScore == null)
            .OrderBy(p => EF.Functions.Random())
            .Take(pool)
            .ToListAsync();

        if (unratedPhotos.Count == 0)
        {
            return Ok(new List<Photo>());
        }

        // 2. 并行计算每张照片的预测分数（使用 CanonPool 控制并发数为 20）
        var photoScoreResults = new List<(Photo Photo, int Score)>();
        var lockObject = new object();

        foreach (var photo in unratedPhotos)
        {
            _canonPool.RegisterNewTaskToPool(async () =>
            {
                var predictedScore = await GuessScoreInternal(photo);
                lock (lockObject)
                {
                    photoScoreResults.Add((photo, predictedScore));
                }
            });
        }

        await _canonPool.RunAllTasksInPoolAsync(20); // 使用 20 个并发线程

        // 3. 按预测分数降序排序，返回前N张
        var result = photoScoreResults
            .OrderByDescending(ps => ps.Score)
            .Take(size)
            .Select(ps => ps.Photo)
            .ToList();

        return Ok(result);
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
    public async Task<ActionResult<List<Photo>>> GetSimilar(int id, [FromQuery] int skip = 0, [FromQuery] int take = 10)
    {
        var targetPhoto = await _context.Photos.FindAsync(id);
        if (targetPhoto == null)
        {
            return NotFound("Target photo not found.");
        }

        var vector = await EnsureFeatureVector(targetPhoto);
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

        return Ok(similarPhotos);
    }

    /// <summary>
    /// 根据相似照片猜测独立分（使用分层KNN平衡算法）
    /// </summary>
    [HttpGet("{id}/guess-score")]
    public async Task<ActionResult<object>> GuessScore(int id)
    {
        var targetPhoto = await _context.Photos.FindAsync(id);
        if (targetPhoto == null)
        {
            return NotFound("Target photo not found.");
        }

        var vector = await EnsureFeatureVector(targetPhoto);
        if (vector == null)
        {
            return BadRequest("Could not ensure feature vector for this photo.");
        }

        var result = await GuessScoreBalancedInternal(targetPhoto, vector);

        return Ok(new
        {
            predictedScore = result.PredictedScore,
            votes = result.Votes
        });
    }

    /// <summary>
    /// 内部方法：猜测单张照片的分数（使用分层KNN平衡算法）
    /// </summary>
    private async Task<int> GuessScoreInternal(Photo targetPhoto)
    {
        var vector = await EnsureFeatureVector(targetPhoto);
        if (vector == null)
        {
            return 0;
        }

        var result = await GuessScoreBalancedInternal(targetPhoto, vector);
        return (int)Math.Round(result.PredictedScore);
    }

    private async Task<(double PredictedScore, Dictionary<int, double> Votes)> GuessScoreBalancedInternal(Photo targetPhoto, byte[] targetVectorBytes)
    {
        // 1. 使用 Window Function 分层获取每个分数段的前20名相似照片
        // 这样可以避免高分照片数量过多导致的样本偏差
        // Added AsNoTracking to avoid tracking overhead and potential navigation property issues
        var similarRatedPhotos = await _context.Photos
            .FromSqlInterpolated($@"
                WITH Ranked AS (
                    SELECT 
                        Id,
                        -- IndependentScore is needed for partitioning
                        IndependentScore,
                        -- Distance is needed for ordering
                        VectorDistance(FeatureVector, {targetVectorBytes}) as Distance,
                        ROW_NUMBER() OVER (
                            PARTITION BY CAST(ROUND(IndependentScore) AS INTEGER) 
                            ORDER BY VectorDistance(FeatureVector, {targetVectorBytes}) ASC
                        ) as Rank
                    FROM Photos
                    WHERE Id != {targetPhoto.Id} 
                      AND FeatureVector IS NOT NULL 
                      AND IndependentScore IS NOT NULL
                )
                SELECT p.* 
                FROM Photos p
                INNER JOIN Ranked r ON p.Id = r.Id
                WHERE r.Rank <= 20")
            .AsNoTracking()
            .ToListAsync();

        if (similarRatedPhotos.Count == 0)
        {
            return (0, new Dictionary<int, double>());
        }

        var targetVector = ImageAnalysisService.ByteArrayToFloatArray(targetVectorBytes);
        
        // 2. 按分数分组计算相关性均值
        // Group by rounded score (0-5)
        var scoreGroups = similarRatedPhotos
            .GroupBy(p => (int)Math.Round(p.IndependentScore!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var scoreConfidences = new Dictionary<int, double>();

        // 遍历可能的 0-5 分
        for (int i = 0; i <= 5; i++)
        {
            if (!scoreGroups.ContainsKey(i))
            {
                scoreConfidences[i] = 0;
                continue;
            }

            var photosInGroup = scoreGroups[i];

            // 【核心优化】：不要算平均值！
            // 如果我是烂片，我可能只跟库里某一张烂片特别像，跟其他烂片不像。
            // 所以取 Top 3 的均值，代表这个分数段的"最佳匹配能力"。
            var similarities = new List<double>();
            foreach (var photo in photosInGroup)
            {
                var photoVector = ImageAnalysisService.ByteArrayToFloatArray(photo.FeatureVector!);
                var similarity = ImageAnalysisService.CalculateCosineSimilarity(targetVector, photoVector);
                similarities.Add(Math.Max(0, similarity));
            }
            
            // 取最像的前3个的平均相似度
            var bestMatchSimilarity = similarities.OrderByDescending(x => x).Take(3).Average();

            // 4. 【关键】：非线性放大
            // 0.8 和 0.85 的差距要变成 1 和 10 的差距
            // 使用指数放大，Base 可以是 30 或者更高
            // 这种算法下，那个拥有"最像的那张图"的分数段，得分会飙升
            scoreConfidences[i] = Math.Pow(bestMatchSimilarity, 30);
        }

        // 5. 加权平均算出最终分
        double totalWeight = scoreConfidences.Values.Sum();
        if (totalWeight == 0) return (0, scoreConfidences);

        double weightedSum = scoreConfidences.Sum(x => x.Key * x.Value);
        var predictedScore = weightedSum / totalWeight;
        
        // 归一化输出用于前端显示（可选，这里保留原始计算值更直观）
        // 为了前端展示方便，我们把置信度归一化到 0-100
        var displayVotes = scoreConfidences.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / totalWeight * 100, 2));

        return (predictedScore, displayVotes);
    }

    private async Task<byte[]?> EnsureFeatureVector(Photo targetPhoto)
    {
        // Lazy generation if vector is missing
        if (targetPhoto.FeatureVector == null)
        {
            var photoRootPath = _configuration["PhotoRootPath"];
            if (string.IsNullOrEmpty(photoRootPath))
            {
                return null;
            }

            var fullPath = Path.Combine(photoRootPath, targetPhoto.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                var vector = _imageAnalysis.GenerateVector(fullPath);
                if (vector != null)
                {
                    targetPhoto.FeatureVector = vector;
                    await _context.SaveChangesAsync();
                    return vector;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating vector for photo {Id}", targetPhoto.Id);
            }

            return null;
        }

        return targetPhoto.FeatureVector;
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