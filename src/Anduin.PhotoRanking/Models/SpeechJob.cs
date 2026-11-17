using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Anduin.PhotoRanking.Models;

public record SpeechJob
{
    [JsonProperty("id"), JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonProperty("status"), JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonProperty("queue_position"), JsonPropertyName("queue_position")] public int? QueuePosition { get; init; }
    [JsonProperty("completed_segments"), JsonPropertyName("completed_segments")] public int CompletedSegments { get; init; }
    [JsonProperty("total_segments"), JsonPropertyName("total_segments")] public int TotalSegments { get; init; }
    [JsonProperty("attempts"), JsonPropertyName("attempts")] public int Attempts { get; init; }
    [JsonProperty("cache_hit"), JsonPropertyName("cache_hit")] public bool CacheHit { get; init; }
}
