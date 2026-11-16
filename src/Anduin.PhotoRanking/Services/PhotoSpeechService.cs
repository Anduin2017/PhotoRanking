using System.Net.Http.Headers;
using Anduin.PhotoRanking.Models;
using Microsoft.Extensions.Options;

namespace Anduin.PhotoRanking.Services;

public class PhotoSpeechService(IHttpClientFactory clients, IOptions<TtsOptions> options)
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<(byte[] Content, string ContentType)> SynthesizeAsync(string summary, CancellationToken ct)
    {
        var settings = options.Value;
        using var client = clients.CreateClient(nameof(PhotoSpeechService));
        client.Timeout = TimeSpan.FromMinutes(3);
        // The native endpoint runs inference in a thread pool, leaving health checks responsive.
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["voice"] = settings.EffectiveVoice,
            ["text"] = summary
        });
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType == null || !contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Speech service did not return audio.");
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        if (content.Length == 0) throw new InvalidDataException("Speech service returned empty audio.");
        return (content, contentType);
    }
}
