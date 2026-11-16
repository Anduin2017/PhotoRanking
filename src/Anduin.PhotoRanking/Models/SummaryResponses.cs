namespace Anduin.PhotoRanking.Models;

public class SummaryResponses
{
    // Keep the existing final-answer field compatible with older clients.
    public string? Summary { get; init; }
    public string? Response1 { get; init; }
    public string? Response2 { get; init; }
}
