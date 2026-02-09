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
    /// 应用 SmoothStep 算法（Hermite 插值）来平滑分数
    /// 公式：t = x/6, y = 6 * (t^2 * (3 - 2t))
    /// 例如：4.0 -> 4.48 (接近 4.5)
    /// </summary>
    private double ApplySmoothStep(double score)
    {
        // 将分数归一化到 [0, 1] 区间
        double t = score / 6.0;
        
        // Hermite 插值公式：t^2 * (3 - 2t)
        double smoothed = t * t * (3.0 - 2.0 * t);
        
        // 还原到 [0, 6] 区间
        return smoothed * 6.0;
    }

    /// <summary>
    /// 为照片打分并更新所有相关分数
    /// </summary>
    public async Task<Photo> RatePhotoAsync(int photoId, int score)
    {
        if (score < 0 || score > 6)
        {
            throw new ArgumentException("Score must be between 0 and 6", nameof(score));
        }

        var photo = await _context.Photos
            .Include(p => p.RatingLogs)
            .Include(p => p.Album)
            .FirstOrDefaultAsync(p => p.Id == photoId);

        if (photo == null)
        {
            throw new InvalidOperationException($"Photo {photoId} not found");
        }

        // 验证打 6 分的条件
        if (score == 6)
        {
            var isEligibleForSix = photo.RatingCount > 8 &&
                                  Math.Round(photo.IndependentScore ?? 0) >= 5 &&
                                  photo.Album.AlbumScore > 4.1;
            
            if (!isEligibleForSix)
            {
                throw new InvalidOperationException("This photo is not eligible for a 6-point rating yet.");
            }
        }

        // 记录打分日志
        var ratingLog = new RatingLog
        {
            PhotoId = photoId,
            Score = score,
            RatedAt = DateTime.UtcNow
        };

        _context.RatingLogs.Add(ratingLog);
        photo.RatingCount++;
        photo.LastRatedAt = DateTime.UtcNow;

        // 检查最后三次打分是否相同
        var lastThreeScores = photo.RatingLogs
            .OrderByDescending(r => r.RatedAt)
            .Take(3)
            .Select(r => r.Score)
            .ToList();

        if (lastThreeScores.Count >= 3 && lastThreeScores.Distinct().Count() == 1)
        {
            photo.IsFixed = true;
            photo.IndependentScore = lastThreeScores[0];
        }
        else if (photo.RatingCount >= 3)
        {
            // 计算独立分（最近的打分）
            photo.IndependentScore = score;
        }
        else
        {
            photo.IndependentScore = score;
        }

        await _context.SaveChangesAsync();

        // 更新相册统计
        await UpdateAlbumScoresAsync(photo.AlbumId);

        // 更新该相册下所有照片的整体分和已知性
        await UpdatePhotoScoresInAlbumAsync(photo.AlbumId);

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
    /// 更新相册分数和统计
    /// </summary>
    private async Task UpdateAlbumScoresAsync(string albumId)
    {
        var album = await _context.Albums
            .Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.AlbumId == albumId);

        if (album == null) return;

        album.PhotoCount = album.Photos.Count();

        var ratedPhotos = album.Photos.Where(p => p.IndependentScore.HasValue).ToList();
        album.KnownRate = album.PhotoCount > 0 ? (double)ratedPhotos.Count / album.PhotoCount : 0;

        // 计算相册分：取前20%高分照片的均值
        if (ratedPhotos.Count > 0)
        {
            var avgRated = ratedPhotos.Average(p => p.IndependentScore!.Value);
            var unratedScore = Math.Max(0, avgRated - 1); // 未打分的照片分数为 avg - 1
            
            // 构建所有照片的分数列表（已评分用独立分，未评分用 unratedScore）
            var allPhotoScores = new List<double>();
            foreach (var photo in album.Photos)
            {
                allPhotoScores.Add(photo.IndependentScore ?? unratedScore);
            }
            
            // 排序并取前80%（至少取1张）
            var sortedScores = allPhotoScores.OrderByDescending(s => s).ToList();
            var top80PercentCount = Math.Max(1, (int)Math.Ceiling(sortedScores.Count * 0.8));
            var topScores = sortedScores.Take(top80PercentCount);
            
            album.AlbumScore = topScores.Average();
        }
        else
        {
            album.AlbumScore = 2.5; // 默认分数
        }

        // 计算标准差、最高分、最低分
        if (album.Photos.Count() > 0)
        {
            var scores = album.Photos.Select(p => p.IndependentScore ?? album.AlbumScore).ToList();
            var mean = scores.Average();
            var variance = scores.Sum(s => Math.Pow(s - mean, 2)) / scores.Count;
            album.StandardDeviation = Math.Sqrt(variance);

            album.HighestScore = scores.Max();
            album.LowestScore = scores.Min();
        }

        album.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 更新相册中所有照片的整体分和已知性
    /// </summary>
    private async Task UpdatePhotoScoresInAlbumAsync(string albumId)
    {
        var album = await _context.Albums
            .Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.AlbumId == albumId);

        if (album == null) return;

        foreach (var photo in album.Photos)
        {
            // 计算整体分：70%独立分 + 30%相册分
            if (photo.IndependentScore.HasValue)
            {
                photo.OverallScore = photo.IndependentScore.Value * 0.7 + album.AlbumScore * 0.3;
            }
            else
            {
                photo.OverallScore = album.AlbumScore;
            }

            // 计算已知性
            var ratingCountScore = Math.Min(photo.RatingCount, 5) * 10.0; // 最多50分
            var albumKnownRateScore = album.KnownRate * 50.0; // 最多50分

            if (photo.IsFixed)
            {
                // 如果已固定（最后三次打分相同），则基础分为50
                photo.Knownness = 50 + albumKnownRateScore;
            }
            else
            {
                photo.Knownness = ratingCountScore + albumKnownRateScore;
            }
        }

        await _context.SaveChangesAsync();
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
    /// 猜测单张照片的分数（使用分层KNN平衡算法 + SmoothStep 平滑）
    /// </summary>
    public async Task<int> GuessScoreInternal(Photo targetPhoto)
    {
        var vector = await EnsureFeatureVectorPublic(targetPhoto);
        if (vector == null)
        {
            return 0;
        }

        var result = await GuessScoreBalancedInternal(targetPhoto, vector);
        return (int)Math.Round(result.PredictedScore);
    }

    /// <summary>
    /// 使用分层KNN平衡算法猜测照片分数，并应用 SmoothStep 平滑
    /// </summary>
    public async Task<(double PredictedScore, Dictionary<int, double> Votes)> GuessScoreBalancedInternal(Photo targetPhoto, byte[] targetVectorBytes)
    {
        // 1. 使用 Window Function 分层获取每个分数段的前20名相似照片
        // 这样可以避免高分照片数量过多导致的样本偏差
        var similarRatedPhotos = await _context.Photos
            .FromSqlInterpolated($@"
                WITH Ranked AS (
                    SELECT 
                        Id,
                        IndependentScore,
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
        var scoreGroups = similarRatedPhotos
            .GroupBy(p => (int)Math.Round(p.IndependentScore!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var scoreConfidences = new Dictionary<int, double>();

        // 遍历可能的 0-6 分
        for (int i = 0; i <= 6; i++)
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

            // 非线性放大：0.8 和 0.85 的差距要变成 1 和 10 的差距
            scoreConfidences[i] = Math.Pow(bestMatchSimilarity, 30);
        }

        // 3. 加权平均算出原始预测分
        double totalWeight = scoreConfidences.Values.Sum();
        if (totalWeight == 0) return (0, scoreConfidences);

        double weightedSum = scoreConfidences.Sum(x => x.Key * x.Value);
        var rawPredictedScore = weightedSum / totalWeight;
        
        // 4. 【新增】应用 SmoothStep 算法平滑分数
        var smoothedScore = ApplySmoothStep(rawPredictedScore);
        
        // 归一化输出用于前端显示
        var displayVotes = scoreConfidences.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / totalWeight * 100, 2));

        return (smoothedScore, displayVotes);
    }

    /// <summary>
    /// 确保照片有特征向量，如果没有则生成（公共方法）
    /// </summary>
    public async Task<byte[]?> EnsureFeatureVectorPublic(Photo targetPhoto)
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
}
