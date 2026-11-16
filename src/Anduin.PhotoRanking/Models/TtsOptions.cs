namespace Anduin.PhotoRanking.Models;

public class TtsOptions
{
    public const string SectionName = "Tts";
    public string Endpoint { get; set; } = "https://tts.aiursoft.com/api/tts";
    // IndexTTS selects a local voice profile, rather than loading a model per request.
    public string Model { get; set; } = "broadcasting";
    public string Voice { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string EffectiveVoice => string.IsNullOrWhiteSpace(Voice) ? Model : Voice;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(EffectiveVoice) && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
