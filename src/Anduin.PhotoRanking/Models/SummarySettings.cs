using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models;

public class SummarySettings
{
    [Required, MaxLength(8000)]
    public required string Prompt1 { get; set; }

    [Required, MaxLength(8000)]
    public required string Prompt2 { get; set; }

    [Required, MaxLength(8000)]
    public required string Prompt3 { get; set; }

    [Range(0, 100)]
    public double TopPercent { get; set; } = 3;
}
