using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

public class ScoringService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ScoringService> _logger;
    
    public ScoringService(AppDbContext context, ILogger<ScoringService> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    /// <summary>
    /// 为照片打分并更新所有相关分数
    /// </summary>
    public async Task<Photo> RatePhotoAsync(int photoId, int score)
    {
        if (score < 0 || score > 5)
        {
            throw new ArgumentException("Score must be between 0 and 5", nameof(score));
        }
        
        var photo = await _context.Photos
            .Include(p => p.RatingLogs)
            .Include(p => p.Album)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        
        if (photo == null)
        {
            throw new InvalidOperationException($"Photo {photoId} not found");
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
        
        // 计算相册分
        if (ratedPhotos.Count > 0)
        {
            var avgRated = ratedPhotos.Average(p => p.IndependentScore!.Value);
            var unratedScore = Math.Max(0, avgRated - 1); // 未打分的照片分数为 avg - 1
            var unratedCount = album.PhotoCount - ratedPhotos.Count;
            
            album.AlbumScore = (ratedPhotos.Sum(p => p.IndependentScore!.Value) + unratedCount * unratedScore) / album.PhotoCount;
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
            // 计算整体分：独立分和相册分的均值
            if (photo.IndependentScore.HasValue)
            {
                photo.OverallScore = (photo.IndependentScore.Value + album.AlbumScore) / 2.0;
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
}
