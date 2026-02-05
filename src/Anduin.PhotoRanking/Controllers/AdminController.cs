using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly SeederService _seederService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AdminController> _logger;
    
    public AdminController(SeederService seederService, AppDbContext dbContext, ILogger<AdminController> logger)
    {
        _seederService = seederService;
        _dbContext = dbContext;
        _logger = logger;
    }
    
    /// <summary>
    /// 获取全局统计信息
    /// </summary>
    [HttpGet("global-stats")]
    public async Task<ActionResult<GlobalStats>> GetGlobalStats()
    {
        var waitingCount = await _dbContext.Photos.CountAsync(p => p.IndependentScore == null);
        var photoScores = await _dbContext.Photos
            .Where(p => p.IndependentScore != null)
            .Select(p => p.IndependentScore!.Value)
            .ToListAsync();

        var albumStats = await _dbContext.Albums
            .Select(a => new { a.KnownRate })
            .ToListAsync();

        var totalPhotos = waitingCount + photoScores.Count;
        var totalAlbums = albumStats.Count;

        var stats = new GlobalStats
        {
            WaitingCount = waitingCount,
            RatedCount = photoScores.Count,
            FullyUnknownAlbumCount = albumStats.Count(a => a.KnownRate < 0.001),
            FullyKnownAlbumCount = albumStats.Count(a => a.KnownRate > 0.999),
            AveragePhotosPerAlbum = totalAlbums > 0 ? (double)totalPhotos / totalAlbums : 0,
            AverageAlbumKnownRate = totalAlbums > 0 ? albumStats.Average(a => a.KnownRate) : 0,
            OverallAverageScore = photoScores.Any() ? photoScores.Average() : 0,
            ScoreDistribution = new Dictionary<int, int>()
        };

        // Initialize 0-5 keys
        for (int i = 0; i <= 5; i++) stats.ScoreDistribution[i] = 0;

        foreach (var score in photoScores)
        {
            var rounded = (int)Math.Round(score);
            if (rounded < 0) rounded = 0;
            if (rounded > 5) rounded = 5;
            stats.ScoreDistribution[rounded]++;
        }

        return Ok(stats);
    }

    /// <summary>
    /// 手动触发数据同步（扫描目录）
    /// </summary>
    [HttpPost("seed")]
    public async Task<ActionResult> TriggerSeed()
    {
        try
        {
            _logger.LogInformation("Manual seed triggered");
            await _seederService.SeedAsync();
            return Ok(new { message = "Seeding completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual seeding");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    /// <summary>
    /// 更新相册统计
    /// </summary>
    [HttpPost("update-album-stats")]
    public async Task<ActionResult> UpdateAlbumStats()
    {
        try
        {
            await _seederService.UpdateAlbumStatsAsync();
            return Ok(new { message = "Album stats updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating album stats");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
