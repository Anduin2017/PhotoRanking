using System.Text.Json.Serialization;

namespace Anduin.PhotoRanking.Models;

public record SpeechJob
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("queue_position")] public int? QueuePosition { get; init; }
    [JsonPropertyName("completed_segments")] public int CompletedSegments { get; init; }
    [JsonPropertyName("total_segments")] public int TotalSegments { get; init; }
    [JsonPropertyName("attempts")] public int Attempts { get; init; }
    [JsonPropertyName("cache_hit")] public bool CacheHit { get; init; }
}
