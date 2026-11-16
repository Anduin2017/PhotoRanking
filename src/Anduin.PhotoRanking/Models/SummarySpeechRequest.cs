using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models;

public class SummarySpeechRequest
{
    [Required]
    public string Summary { get; set; } = string.Empty;
}
