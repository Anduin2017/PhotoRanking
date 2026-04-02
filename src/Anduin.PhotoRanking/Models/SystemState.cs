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
}
