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
public class SummaryController(PhotoSummaryService service, AppDbContext context,
    PhotoSpeechService speech, ILogger<SummaryController> logger) : ControllerBase
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
            generation = service.GetProgress(state.SummaryPromptsModifiedAt),
            examples,
            targetCount = await target.CountAsync(ct),
            completedCount = await target.CountAsync(p => p.Summary != null && p.SummaryResponse1 != null &&
                p.SummaryResponse2 != null && p.SummaryGeneratedAt != null &&
                (p.SummaryCheckpointPromptsModifiedAt == null || p.SummaryCompletedRounds == 3) &&
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
        return Ok(await service.GetResponsesAsync(photoId, ct));
    }

    [HttpPost("photos/{photoId:int}/speech")]
    public Task<IActionResult> GetSpeech(int photoId, SummarySpeechRequest request, CancellationToken ct)
        => WithCurrentSummary(photoId, request, async summary =>
        {
            var audio = await speech.SynthesizeAsync(photoId, summary, ct);
            return File(audio.Content, audio.ContentType);
        }, ct);

    [HttpPost("photos/{photoId:int}/speech/jobs")]
    public Task<IActionResult> CreateSpeechJob(int photoId, SummarySpeechRequest request, CancellationToken ct)
        => WithCurrentSummary(photoId, request, async summary =>
            StatusCode(202, await speech.SubmitAsync(photoId, summary, ct)), ct);

    [HttpPost("photos/{photoId:int}/speech/jobs/{jobId}")]
    public Task<IActionResult> GetSpeechJob(int photoId, string jobId, SummarySpeechRequest request, CancellationToken ct)
        => WithCurrentSummary(photoId, request, async summary =>
            Ok(await speech.StatusAsync(photoId, summary, jobId, ct)), ct);

    [HttpPost("photos/{photoId:int}/speech/jobs/{jobId}/audio")]
    public Task<IActionResult> GetSpeechAudio(int photoId, string jobId, SummarySpeechRequest request, CancellationToken ct)
        => WithCurrentSummary(photoId, request, async summary =>
        {
            var audio = await speech.AudioAsync(photoId, summary, jobId, ct);
            return File(audio.Content, audio.ContentType);
        }, ct);

    private async Task<IActionResult> WithCurrentSummary(int photoId, SummarySpeechRequest request,
        Func<string, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!await context.Photos.AnyAsync(p => p.Id == photoId, ct)) return NotFound();
        var summary = await service.GetSummaryAsync(photoId, ct);
        if (string.IsNullOrWhiteSpace(summary)) return NotFound();
        if (request.Summary != summary) return Conflict(new { error = "The summary has changed." });
        if (!speech.IsConfigured) return StatusCode(503, new { error = "Speech is not configured." });
        try
        {
            var result = await action(summary);
            if (await service.GetSummaryAsync(photoId, ct) != summary)
                return Conflict(new { error = "The summary has changed." });
            return result;
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Speech request timed out." });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Speech request failed with status {Status} for photo {PhotoId}.", ex.StatusCode, photoId);
            var status = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => 429,
                System.Net.HttpStatusCode.NotFound => 404,
                System.Net.HttpStatusCode.Gone => 410,
                System.Net.HttpStatusCode.Conflict => 409,
                _ => 502
            };
            return StatusCode(status, new { error = "Speech request failed. Please retry." });
        }
        catch (InvalidDataException)
        {
            return StatusCode(502, new { error = "Speech service returned an invalid response." });
        }
        catch (System.Text.Json.JsonException)
        {
            return StatusCode(502, new { error = "Speech service returned an invalid response." });
        }
    }
}
