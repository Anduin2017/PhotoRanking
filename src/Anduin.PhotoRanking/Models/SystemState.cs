using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models;

/// <summary>
/// 系统全局状态，用于追踪后台任务的触发
/// </summary>
public class SystemState
{
    [Key]
    public int Id { get; init; } = 1;

    /// <summary>
    /// 全局最后一次打分时间
    /// </summary>
    public DateTime LastRatingAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// 上次全量推测分更新完成时间
    /// </summary>
    public DateTime LastGlobalScoringAt { get; set; } = DateTime.MinValue;

    [MaxLength(8000)]
    public string SummaryPrompt1 { get; set; } = "Describe what is visible in this image for a blind person.";

    [MaxLength(8000)]
    public string SummaryPrompt2 { get; set; } = "What does this photograph express? Discuss its composition, mood and quality. Distinguish observation from interpretation.";

    [MaxLength(8000)]
    public string SummaryPrompt3 { get; set; } = "Write a poem in the voice of a person or subject in this photograph. Treat their thoughts as creative imagination. Return only the poem.";

    public double SummaryTopPercent { get; set; } = 3;

    public DateTime SummaryPromptsModifiedAt { get; set; } = DateTime.MinValue;
}
