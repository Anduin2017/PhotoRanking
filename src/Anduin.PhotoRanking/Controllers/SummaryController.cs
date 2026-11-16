using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EFCoreSecondLevelCacheInterceptor;

namespace Anduin.PhotoRanking.Controllers;

// Like the other private-library APIs, this is behind the deployment's login proxy.
[ApiController]
[Route("api/summary")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SummaryController(PhotoSummaryService service, AppDbContext context) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var state = await service.GetSettingsAsync(ct);
        var target = await service.GetTargetPhotosAsync(state, ct);
        var examples = await context.Photos.AsNoTracking().NotCacheable()
            .Where(p => p.Summary != null && p.SummaryGeneratedAt != null &&
                p.SummaryGeneratedAt >= state.SummaryPromptsModifiedAt)
            .OrderByDescending(p => p.SummaryGeneratedAt).ThenBy(p => p.Id)
            .Take(6).ToListAsync(ct);
        return Ok(new
        {
            prompt1 = state.SummaryPrompt1,
            prompt2 = state.SummaryPrompt2,
            prompt3 = state.SummaryPrompt3,
            topPercent = state.SummaryTopPercent,
            promptsModifiedAt = state.SummaryPromptsModifiedAt,
            configured = service.IsConfigured,
            examples,
            targetCount = await target.CountAsync(ct),
            completedCount = await target.CountAsync(p => p.Summary != null && p.SummaryGeneratedAt != null &&
                p.SummaryGeneratedAt >= state.SummaryPromptsModifiedAt, ct)
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(SummarySettings settings, CancellationToken ct)
    {
        if (!double.IsFinite(settings.TopPercent) || string.IsNullOrWhiteSpace(settings.Prompt1) ||
            string.IsNullOrWhiteSpace(settings.Prompt2) || string.IsNullOrWhiteSpace(settings.Prompt3))
            return BadRequest(new { error = "Three non-empty prompts and a percentage from 0 to 100 are required." });
        await service.SaveSettingsAsync(settings, ct);
        return await GetSettings(ct);
    }

    [HttpGet("photos/{photoId:int}")]
    public async Task<IActionResult> GetSummary(int photoId, CancellationToken ct)
    {
        if (!await context.Photos.AnyAsync(p => p.Id == photoId, ct)) return NotFound();
        return Ok(new { summary = await service.GetSummaryAsync(photoId, ct) });
    }
}
