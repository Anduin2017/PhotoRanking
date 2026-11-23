using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Anduin.PhotoRanking.Models;
using Microsoft.Extensions.Options;

namespace Anduin.PhotoRanking.Services;

public class PhotoSpeechService(IHttpClientFactory clients, IOptions<TtsOptions> options)
{
    public bool IsConfigured => options.Value.IsConfigured;

    private Uri JobsUri => new(new Uri(options.Value.Endpoint), "jobs");

    private string Signature(int photoId, string summary, string jobId)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey),
            Encoding.UTF8.GetBytes($"PhotoRanking speech\0{photoId}\0{jobId}\0{summary}")));

    private string NativeId(int photoId, string summary, string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length != 32 || !parts[0].All(char.IsAsciiHexDigit) ||
            parts[1].Length != 64 || !parts[1].All(char.IsAsciiHexDigit) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]),
                Convert.FromHexString(Signature(photoId, summary, parts[0]))))
            throw new ArgumentException("Invalid speech job.", nameof(token));
        return parts[0];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var client = clients.CreateClient(nameof(PhotoSpeechService));
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = 128 * 1024 * 1024;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException("Speech gateway request failed.", null, status);
    }

    private SpeechJob BoundJob(SpeechJob? job, int photoId, string summary, string? expectedId = null)
    {
        if (job is null || job.Id.Length != 32 || !job.Id.All(char.IsAsciiHexDigit) ||
            (expectedId != null && job.Id != expectedId) ||
            job.Status is not ("queued" or "running" or "completed" or "failed" or "expired"))
            throw new InvalidDataException("Invalid speech job response.");
        // Bind the upstream job to the requested photo and exact final answer without storing secrets or jobs locally.
        return job with { Id = job.Id + "." + Signature(photoId, summary, job.Id) };
    }

    private static string AddSpeechPauses(string summary)
    {
        const string pausePunctuation = "，。；：、！？….,;:!?";
        var lines = summary.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            lines[i] = line.Length > 0 && !pausePunctuation.Contains(line[^1]) ? line + "；" : line;
        }
        return string.Join('\n', lines);
    }

    public async Task<SpeechJob> SubmitAsync(int photoId, string summary, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, JobsUri);
        request.Content = JsonContent.Create(new
        {
            voice = options.Value.EffectiveVoice,
            text = AddSpeechPauses(summary),
            response_format = "wav"
        });
        using var response = await SendAsync(request, ct);
        return BoundJob(await response.Content.ReadFromJsonAsync<SpeechJob>(ct), photoId, summary);
    }

    public async Task<SpeechJob> StatusAsync(int photoId, string summary, string token, CancellationToken ct)
    {
        var id = NativeId(photoId, summary, token);
        using var request = new HttpRequestMessage(HttpMethod.Get, JobsUri + "/" + id);
        using var response = await SendAsync(request, ct);
        return BoundJob(await response.Content.ReadFromJsonAsync<SpeechJob>(ct), photoId, summary, id);
    }

    public async Task<(byte[] Content, string ContentType)> AudioAsync(int photoId, string summary, string token, CancellationToken ct)
    {
        var id = NativeId(photoId, summary, token);
        using var request = new HttpRequestMessage(HttpMethod.Get, JobsUri + "/" + id + "/audio");
        using var response = await SendAsync(request, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == null || !contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Speech service did not return audio.");
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        if (content.Length == 0) throw new InvalidDataException("Speech service returned empty audio.");
        return (content, contentType);
    }

    public async Task<(byte[] Content, string ContentType)> SynthesizeAsync(int photoId, string summary, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var job = await SubmitAsync(photoId, summary, deadline.Token);
        while (job.Status is "queued" or "running")
        {
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            job = await StatusAsync(photoId, summary, job.Id, deadline.Token);
        }
        if (job.Status != "completed") throw new InvalidDataException("Speech generation failed.");
        return await AudioAsync(photoId, summary, job.Id, deadline.Token);
    }
}
