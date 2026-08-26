using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

public class ScoringService
{
    private readonly AppDbContext _context;
    private readonly ImageAnalysisService _imageAnalysis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScoringService> _logger;

    public ScoringService(
        AppDbContext context,
        ImageAnalysisService imageAnalysis,
        IConfiguration configuration,
        ILogger<ScoringService> logger)
    {
        _context = context;
        _imageAnalysis = imageAnalysis;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 覆盖照片的最终人工分。评分历史、评分次数和相册都不得改变最终分。
    /// </summary>
    public async Task<Photo> RatePhotoAsync(int photoId, int score)
    {
        if (score < 0 || score > 6)
        {
            throw new ArgumentException("Score must be between 0 and 6", nameof(score));
        }

        var photo = await _context.Photos
            .Include(p => p.Album)
            .FirstOrDefaultAsync(p => p.Id == photoId);

        if (photo == null)
        {
            throw new InvalidOperationException($"Photo {photoId} not found");
        }

        var previousScore = photo.IndependentScore;
        var ratingLog = new RatingLog
        {
            PhotoId = photoId,
            Score = score,
            PreviousScore = previousScore,
            PredictionAtRating = previousScore.HasValue ? null : photo.EstimatedScore,
            PredictionModelVersion = previousScore.HasValue ? null : photo.EstimatedScoreModelVersion,
            IsCorrection = previousScore.HasValue,
            RatedAt = DateTime.UtcNow
        };

        _context.RatingLogs.Add(ratingLog);

        // Old schema columns are neutralized for rolling-upgrade compatibility only.
        // RatingCount is intentionally left frozen: repeated ratings are corrections.
        photo.IsFixed = false;
        photo.Knownness = 0;
        photo.LastRatedAt = DateTime.UtcNow;
        photo.IndependentScore = score;
        photo.OverallScore = score;

        var state = await _context.SystemStates.FirstOrDefaultAsync();
        if (state == null)
        {
            state = new SystemState();
            _context.SystemStates.Add(state);
        }

        state.LastRatingAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 相册数据现在只是报表统计，绝不再回灌任何照片。
        await UpdateAlbumScoreAsync(photo.AlbumId);

        // 重新获取更新后的照片
        photo = await _context.Photos
            .Include(p => p.Album)
            .FirstAsync(p => p.Id == photoId);

        return photo;
    }

    /// <summary>
    /// 增加照片浏览次数
    /// </summary>
    public async Task IncrementViewCountAsync(int photoId)
    {
        var photo = await _context.Photos.FindAsync(photoId);
        if (photo != null)
        {
            photo.ViewCount++;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 更新相册报表统计。AlbumScore 是贝叶斯修正后的人工均分，不参与照片评分。
    /// </summary>
    public async Task UpdateAlbumScoreAsync(string albumId)
    {
        var album = await _context.Albums.FirstOrDefaultAsync(a => a.AlbumId == albumId);

        if (album == null) return;

        var summary = await _context.Photos
            .Where(p => p.AlbumId == albumId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PhotoCount = g.Count(),
                RatedCount = g.Count(p => p.IndependentScore != null),
                Average = g.Where(p => p.IndependentScore != null).Average(p => p.IndependentScore),
                Highest = g.Max(p => p.IndependentScore),
                Lowest = g.Min(p => p.IndependentScore),
                AverageSquare = g.Where(p => p.IndependentScore != null)
                    .Average(p => p.IndependentScore * p.IndependentScore)
            })
            .FirstOrDefaultAsync();

        var globalAverage = await _context.Photos
            .Where(p => p.IndependentScore != null)
            .AverageAsync(p => (double?)p.IndependentScore) ?? 3.0;

        ApplyAlbumSummary(
            album,
            summary?.PhotoCount ?? 0,
            summary?.RatedCount ?? 0,
            summary?.Average,
            summary?.AverageSquare,
            summary?.Highest,
            summary?.Lowest,
            globalAverage);

        album.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 批量重建所有相册报表统计，供文件同步和管理员使用。
    /// </summary>
    public async Task RebuildAllAlbumStatsAsync()
    {
        var globalAverage = await _context.Photos
            .Where(p => p.IndependentScore != null)
            .AverageAsync(p => (double?)p.IndependentScore) ?? 3.0;

        var summaries = await _context.Photos
            .GroupBy(p => p.AlbumId)
            .Select(g => new
            {
                AlbumId = g.Key,
                PhotoCount = g.Count(),
                RatedCount = g.Count(p => p.IndependentScore != null),
                Average = g.Where(p => p.IndependentScore != null).Average(p => p.IndependentScore),
                Highest = g.Max(p => p.IndependentScore),
                Lowest = g.Min(p => p.IndependentScore),
                AverageSquare = g.Where(p => p.IndependentScore != null)
                    .Average(p => p.IndependentScore * p.IndependentScore)
            })
            .ToDictionaryAsync(x => x.AlbumId);

        var albums = await _context.Albums.ToListAsync();
        foreach (var album in albums)
        {
            if (summaries.TryGetValue(album.AlbumId, out var summary))
            {
                ApplyAlbumSummary(album, summary.PhotoCount, summary.RatedCount, summary.Average,
                    summary.AverageSquare, summary.Highest, summary.Lowest, globalAverage);
            }
            else
            {
                ApplyAlbumSummary(album, 0, 0, null, null, null, null, globalAverage);
            }
            album.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    private static void ApplyAlbumSummary(
        Album album,
        int photoCount,
        int ratedCount,
        double? average,
        double? averageSquare,
        double? highest,
        double? lowest,
        double globalAverage)
    {
        const double priorStrength = 5.0;

        album.PhotoCount = photoCount;
        album.RatedPhotoCount = ratedCount;
        album.KnownRate = photoCount > 0 ? (double)ratedCount / photoCount : 0;
        album.AverageManualScore = average;
        album.AlbumScore = ratedCount > 0
            ? (average!.Value * ratedCount + globalAverage * priorStrength) / (ratedCount + priorStrength)
            : globalAverage;
        album.HighestScore = highest;
        album.LowestScore = lowest;
        album.StandardDeviation = average.HasValue && averageSquare.HasValue
            ? Math.Sqrt(Math.Max(0, averageSquare.Value - average.Value * average.Value))
            : 0;
    }

    /// <summary>
    /// 概率密度分布选择（加权随机）
    /// </summary>
    public T WeightedRandomSelect<T>(List<T> items, Func<T, double> weightSelector)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("No items to select from");
        }

        var totalWeight = items.Sum(weightSelector);
        if (totalWeight <= 0)
        {
            // 如果所有权重都是0或负数，随机选择
            return items[Random.Shared.Next(items.Count)];
        }

        var randomValue = Random.Shared.NextDouble() * totalWeight;
        var cumulativeWeight = 0.0;

        foreach (var item in items)
        {
            cumulativeWeight += weightSelector(item);
            if (randomValue <= cumulativeWeight)
            {
                return item;
            }
        }

        return items.Last();
    }

    /// <summary>
    /// 确保照片有特征向量，如果没有则生成（公共方法）
    /// </summary>
    public async Task<byte[]?> EnsureFeatureVectorPublic(Photo targetPhoto, bool save = true)
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
            if (!File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                var vector = _imageAnalysis.GenerateVector(fullPath);
                if (vector != null)
                {
                    targetPhoto.FeatureVector = vector;
                    if (save)
                    {
                        await _context.SaveChangesAsync();
                    }
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
}
