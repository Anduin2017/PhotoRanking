namespace Anduin.PhotoRanking.Models;

public class SummaryOptions
{
    public const string SectionName = "Summary";

    public string Endpoint { get; set; } = "https://ollama.aiursoft.com/api/chat";
    public string Model { get; set; } = "aiursoft-localization:latest";
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model) && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
