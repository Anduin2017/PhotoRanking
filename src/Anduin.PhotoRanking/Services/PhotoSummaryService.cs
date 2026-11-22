using System.Net.Http.Headers;
using System.Text;
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
    ILogger<PhotoSummaryService> logger, SummaryProgress? progress = null)
{
    private readonly SummaryProgress _progress = progress ?? new SummaryProgress();

    public SummaryGenerationStatus GetProgress(DateTime version) => _progress.Get(version);
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
        _progress.Invalidate(state.SummaryPromptsModifiedAt);
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
                Summary = p.Summary,
                Response1 = p.SummaryCheckpointPromptsModifiedAt != null && p.SummaryCompletedRounds < 3
                    ? null : p.SummaryResponse1,
                Response2 = p.SummaryCheckpointPromptsModifiedAt != null && p.SummaryCompletedRounds < 3
                    ? null : p.SummaryResponse2
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
        if (_progress.Get(settings.SummaryPromptsModifiedAt).NextRetryAt > DateTime.UtcNow) return;
        var target = await GetTargetPhotosAsync(settings, ct);
        var pendingIds = await target
            .Where(p => p.Summary == null || p.SummaryResponse1 == null || p.SummaryResponse2 == null ||
                        p.SummaryGeneratedAt == null ||
                        p.SummaryGeneratedAt < settings.SummaryPromptsModifiedAt ||
                        (p.SummaryCheckpointPromptsModifiedAt != null && p.SummaryCompletedRounds < 3))
            .OrderByDescending(p => p.SummaryCheckpointPromptsModifiedAt == settings.SummaryPromptsModifiedAt &&
                                    p.SummaryCompletedRounds > 0)
            .ThenByDescending(p => p.EstimatedScore).ThenBy(p => p.Id)
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
            catch (OperationCanceledException)
            {
                // A local prompt edit cancels the old in-flight round. SQL guards also
                // protect checkpoints if a different application instance edits prompts.
                return;
            }
            catch (TimeoutException ex)
            {
                _progress.Failed($"The model round exceeded {options.Value.TimeoutSeconds} seconds. Saved rounds will be reused.",
                    options.Value.RetryDelaySeconds);
                logger.LogWarning(ex, "Summary round timed out for photo {PhotoId}; saved rounds will be reused.", id);
                return;
            }
            catch (HttpRequestException ex)
            {
                var status = ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode.Value}" : "a connection error";
                _progress.Failed($"The model service returned {status}. Saved rounds will be reused.",
                    options.Value.RetryDelaySeconds);
                logger.LogWarning(ex, "Summary gateway failed for photo {PhotoId}; backing off before retry.", id);
                // A shared backend failure must not cause thousands of immediate retries.
                return;
            }
            catch (IOException ex)
            {
                _progress.Failed("The model connection was interrupted. Saved rounds will be reused.",
                    options.Value.RetryDelaySeconds);
                logger.LogWarning(ex, "Summary stream was interrupted for photo {PhotoId}.", id);
                return;
            }
            catch (Exception ex)
            {
                _progress.Failed(ex is InvalidDataException
                    ? "The model response was incomplete or exceeded its output budget. Saved rounds will be reused."
                    : "The photo could not be summarized. Saved rounds will be reused.", options.Value.RetryDelaySeconds);
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

        var answers = new List<string>();
        if (photo.SummaryCheckpointPromptsModifiedAt == settings.SummaryPromptsModifiedAt)
        {
            if (photo.SummaryCompletedRounds >= 1 && !string.IsNullOrWhiteSpace(photo.SummaryResponse1))
                answers.Add(photo.SummaryResponse1);
            if (answers.Count == 1 && photo.SummaryCompletedRounds >= 2 && !string.IsNullOrWhiteSpace(photo.SummaryResponse2))
                answers.Add(photo.SummaryResponse2);
        }

        using var photoCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _progress.BeginPhoto(photo.Id, settings.SummaryPromptsModifiedAt, answers.Count, photoCancellation);
        try
        {
            var prompts = new[] { settings.SummaryPrompt1, settings.SummaryPrompt2, settings.SummaryPrompt3 };
            var messages = new List<object>();
            for (var i = 0; i < prompts.Length; i++)
            {
                if ((await GetSettingsAsync(ct)).SummaryPromptsModifiedAt != settings.SummaryPromptsModifiedAt) return;
                messages.Add(i == 0
                    ? new { role = "user", content = prompts[i], images = new[] { base64 } }
                    : new { role = "user", content = prompts[i] });
                if (i < answers.Count)
                {
                    messages.Add(new { role = "assistant", content = answers[i] });
                    continue;
                }

                _progress.BeginRound(i + 1);
                var answer = await ReadRoundAsync(messages, photoCancellation.Token);
                photoCancellation.Token.ThrowIfCancellationRequested();
                var eligible = context.Photos.Where(p => p.Id == photo.Id &&
                    context.SystemStates.Any(s => s.SummaryPromptsModifiedAt == settings.SummaryPromptsModifiedAt));
                if (i == 0)
                {
                    // Do not overwrite a checkpoint published by another worker.
                    eligible = eligible.Where(p => p.SummaryCheckpointPromptsModifiedAt != settings.SummaryPromptsModifiedAt ||
                        p.SummaryCompletedRounds == 0);
                }
                else
                {
                    var first = answers[0];
                    var previousRound = i;
                    eligible = eligible.Where(p => p.SummaryCheckpointPromptsModifiedAt == settings.SummaryPromptsModifiedAt &&
                        p.SummaryCompletedRounds == previousRound && p.SummaryResponse1 == first);
                    if (i == 2)
                    {
                        var second = answers[1];
                        eligible = eligible.Where(p => p.SummaryResponse2 == second);
                    }
                }

                var saved = i switch
                {
                    0 => await eligible.ExecuteUpdateAsync(u => u
                        .SetProperty(p => p.SummaryResponse1, answer).SetProperty(p => p.SummaryResponse2, (string?)null)
                        .SetProperty(p => p.SummaryCheckpointPromptsModifiedAt, settings.SummaryPromptsModifiedAt)
                        .SetProperty(p => p.SummaryCompletedRounds, 1), ct),
                    1 => await eligible.ExecuteUpdateAsync(u => u.SetProperty(p => p.SummaryResponse2, answer)
                        .SetProperty(p => p.SummaryCompletedRounds, 2), ct),
                    _ => await eligible.ExecuteUpdateAsync(u => u.SetProperty(p => p.Summary, answer)
                        .SetProperty(p => p.SummaryGeneratedAt, DateTime.UtcNow)
                        .SetProperty(p => p.SummaryCompletedRounds, 3), ct)
                };
                if (saved == 0) return;
                answers.Add(answer);
                messages.Add(new { role = "assistant", content = answer });
                _progress.RoundSaved(i + 1);
                logger.LogInformation("Saved summary round {Round} for photo {PhotoId}.", i + 1, photo.Id);
            }
            logger.LogInformation("Summarized photo {PhotoId}.", photo.Id);
        }
        finally
        {
            _progress.EndPhoto(photoCancellation);
        }
    }

    private async Task<string> ReadRoundAsync(List<object> messages, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var client = httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = options.Value.Model, stream = true, think = false, messages,
            options = new { num_predict = options.Value.MaxOutputTokens }
        });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var reader = new StreamReader(stream);
            var answer = new StringBuilder();
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length > 1_000_000) throw new InvalidDataException("Model response exceeded its size limit.");
                var chunk = JObject.Parse(line);
                if (chunk["error"] != null) throw new InvalidDataException("The model returned an error.");
                var content = chunk["message"]?["content"]?.Value<string>();
                if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(chunk["message"]?["thinking"]?.Value<string>()))
                    _progress.TokenReceived();
                // Ollama normally ends with an empty terminal message. The gateway
                // can include the complete answer again in that terminal message.
                var done = chunk["done"]?.Value<bool>() == true;
                if (done && !string.IsNullOrEmpty(content) && answer.Length > 0 &&
                    content.StartsWith(answer.ToString(), StringComparison.Ordinal))
                    answer.Clear().Append(content);
                else
                    answer.Append(content);
                if (answer.Length > 1_000_000) throw new InvalidDataException("Model response exceeded its size limit.");
                if (!done) continue;
                var reason = chunk["done_reason"]?.Value<string>();
                if (reason is "length" or "max_tokens" ||
                    (string.IsNullOrEmpty(reason) && chunk["eval_count"]?.Value<int>() >= options.Value.MaxOutputTokens))
                    throw new InvalidDataException("The model exhausted its output budget.");
                if (answer.Length == 0 || string.IsNullOrWhiteSpace(answer.ToString()))
                    throw new InvalidDataException("The model returned an empty answer.");
                return answer.ToString();
            }
            throw new InvalidDataException("The model response ended before completion.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The model round exceeded its configured deadline.");
        }
    }
}
