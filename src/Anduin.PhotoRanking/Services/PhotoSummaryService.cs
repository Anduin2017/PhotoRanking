using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;
using EFCoreSecondLevelCacheInterceptor;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace Anduin.PhotoRanking.Services;

public class PhotoSummaryService(
    AppDbContext context,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<SummaryOptions> options,
    ILogger<PhotoSummaryService> logger)
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<SystemState> GetSettingsAsync(CancellationToken ct)
    {
        var state = await context.SystemStates.AsNoTracking().NotCacheable().FirstOrDefaultAsync(ct);
        return state ?? new SystemState();
    }

    public async Task SaveSettingsAsync(SummarySettings settings, CancellationToken ct)
    {
        // Serialize settings saves, including the freshness watermark, in SQLite.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var state = await context.SystemStates.NotCacheable().FirstOrDefaultAsync(ct);
        if (state == null)
        {
            state = new SystemState();
            context.SystemStates.Add(state);
        }

        if (state.SummaryPrompt1 != settings.Prompt1 || state.SummaryPrompt2 != settings.Prompt2 ||
            state.SummaryPrompt3 != settings.Prompt3)
        {
            state.SummaryPrompt1 = settings.Prompt1;
            state.SummaryPrompt2 = settings.Prompt2;
            state.SummaryPrompt3 = settings.Prompt3;
            state.SummaryPromptsModifiedAt = new DateTime(
                Math.Max(DateTime.UtcNow.Ticks, state.SummaryPromptsModifiedAt.Ticks + 1), DateTimeKind.Utc);
        }

        state.SummaryTopPercent = settings.TopPercent;
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IQueryable<Photo>> GetTargetPhotosAsync(SystemState settings, CancellationToken ct)
    {
        var total = await context.Photos.NotCacheable().CountAsync(ct);
        var count = (int)Math.Ceiling(total * settings.SummaryTopPercent / 100);
        // DisplayScore is not mapped. Express the identical coalesce in SQL.
        // Photos without either score wait until a score becomes available.
        return context.Photos.AsNoTracking().NotCacheable()
            .Where(p => p.IndependentScore != null || p.EstimatedScore != null)
            .OrderByDescending(p => p.IndependentScore ?? p.EstimatedScore)
            .ThenBy(p => p.Id)
            .Take(count);
    }

    public async Task<string?> GetSummaryAsync(int photoId, CancellationToken ct)
        => (await GetResponsesAsync(photoId, ct)).Summary;

    public async Task<SummaryResponses> GetResponsesAsync(int photoId, CancellationToken ct)
    {
        // One SQL statement checks all responses against the same prompt watermark.
        return await context.Photos.NotCacheable().Where(p => p.Id == photoId && p.Summary != null &&
                p.SummaryGeneratedAt != null && p.SummaryGeneratedAt >=
                (context.SystemStates.Select(s => (DateTime?)s.SummaryPromptsModifiedAt).FirstOrDefault()
                 ?? DateTime.MinValue))
            .Select(p => new SummaryResponses
            {
                Summary = p.Summary, Response1 = p.SummaryResponse1, Response2 = p.SummaryResponse2
            }).FirstOrDefaultAsync(ct) ?? new SummaryResponses();
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!IsConfigured) return;
        if (!await context.SystemStates.NotCacheable().AnyAsync(ct))
        {
            context.SystemStates.Add(new SystemState());
            await context.SaveChangesAsync(ct);
        }
        var settings = await GetSettingsAsync(ct);
        if (settings.SummaryTopPercent <= 0) return;
        var target = await GetTargetPhotosAsync(settings, ct);
        var pendingIds = await target
            .Where(p => p.Summary == null || p.SummaryResponse1 == null || p.SummaryResponse2 == null ||
                        p.SummaryGeneratedAt == null ||
                        p.SummaryGeneratedAt < settings.SummaryPromptsModifiedAt)
            .Select(p => p.Id).ToListAsync(ct);

        foreach (var id in pendingIds)
        {
            ct.ThrowIfCancellationRequested();
            var current = await GetSettingsAsync(ct);
            // Recalculate the target on the next sweep after any settings change.
            if (current.SummaryPromptsModifiedAt != settings.SummaryPromptsModifiedAt ||
                !current.SummaryTopPercent.Equals(settings.SummaryTopPercent)) return;
            var photo = await context.Photos.AsNoTracking().NotCacheable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (photo == null) continue;
            try
            {
                await GenerateAsync(photo, settings, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Summary gateway rejected the sweep with status {Status}; will retry later.", ex.StatusCode);
                return;
            }
            catch (Exception ex)
            {
                // Do not persist failures as successful summaries or starve later photos.
                logger.LogWarning(ex, "Could not summarize photo {PhotoId}; will retry next sweep.", id);
            }
        }
    }

    private async Task GenerateAsync(Photo photo, SystemState settings, CancellationToken ct)
    {
        var root = Path.GetFullPath(configuration["PhotoRootPath"] ?? "/photos");
        var path = Path.GetFullPath(Path.Combine(root, photo.FilePath));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)) throw new InvalidOperationException("Invalid photo path.");

        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException("Cannot decode photo.");
        var scale = Math.Min(1d, 1536d / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = bitmap.Resize(new SKSizeI(Math.Max(1, (int)(bitmap.Width * scale)),
            Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidOperationException("Cannot resize photo.");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var base64 = Convert.ToBase64String(encoded.ToArray());

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        var messages = new List<object>();
        var prompts = new[] { settings.SummaryPrompt1, settings.SummaryPrompt2, settings.SummaryPrompt3 };
        var answers = new List<string>();
        for (var i = 0; i < prompts.Length; i++)
        {
            if ((await GetSettingsAsync(ct)).SummaryPromptsModifiedAt != settings.SummaryPromptsModifiedAt) return;
            messages.Add(i == 0
                ? new { role = "user", content = prompts[i], images = new[] { base64 } }
                : new { role = "user", content = prompts[i] });
            using var response = await client.PostAsJsonAsync(
                options.Value.Endpoint,
                new { model = options.Value.Model, stream = false, messages }, ct);
            response.EnsureSuccessStatusCode();
            var body = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
            var answer = body["message"]?["content"]?.Value<string>();
            if (body["error"] != null || body["done"]?.Value<bool>() != true || string.IsNullOrWhiteSpace(answer))
                throw new InvalidOperationException("The summary model did not return a complete answer.");
            answers.Add(answer);
            messages.Add(new { role = "assistant", content = answer });
        }

        var generatedAt = DateTime.UtcNow;
        // Atomic guard: a prompt edit while the last request runs must not revive an old poem.
        var saved = await context.Photos.Where(p => p.Id == photo.Id &&
                context.SystemStates.Any(s => s.SummaryPromptsModifiedAt == settings.SummaryPromptsModifiedAt))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Summary, answers[2])
                .SetProperty(p => p.SummaryResponse1, answers[0])
                .SetProperty(p => p.SummaryResponse2, answers[1])
                .SetProperty(p => p.SummaryGeneratedAt, generatedAt), ct);
        if (saved > 0) logger.LogInformation("Summarized photo {PhotoId}.", photo.Id);
    }
}
