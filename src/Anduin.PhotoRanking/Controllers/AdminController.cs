using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc;

namespace Anduin.PhotoRanking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly SeederService _seederService;
    private readonly ILogger<AdminController> _logger;
    
    public AdminController(SeederService seederService, ILogger<AdminController> logger)
    {
        _seederService = seederService;
        _logger = logger;
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
