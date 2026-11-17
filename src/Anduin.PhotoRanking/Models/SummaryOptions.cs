using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models;

public class SummaryOptions
{
    public const string SectionName = "Summary";

    public string Endpoint { get; set; } = "https://ollama.aiursoft.com/api/chat";
    public string Model { get; set; } = "aiursoft-localization:latest";
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 7200)]
    public int TimeoutSeconds { get; set; } = 900;

    [Range(1, 16384)]
    public int MaxOutputTokens { get; set; } = 4096;

    [Range(0, 600)]
    public int RetryDelaySeconds { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model) && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
