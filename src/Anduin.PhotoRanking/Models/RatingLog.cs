using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Anduin.PhotoRanking.Models;

public class RatingLog
{
    // [规则 1.1, 1.2, 1.3] 主键：int, Key, init
    [Key]
    public int Id { get; init; }
    
    // [规则 2.2] 外键ID：required int
    public required int PhotoId { get; set; }
    
    /// <summary>
    /// 打分（0-6）
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 本次纠错前的最终分。历史值只用于审计，绝不参与训练。
    /// </summary>
    public double? PreviousScore { get; set; }

    /// <summary>
    /// 用户评分前，线上模型对该照片的预测。用于无泄漏地评估模型。
    /// </summary>
    public double? PredictionAtRating { get; set; }

    [MaxLength(100)]
    public string? PredictionModelVersion { get; set; }

    public bool IsCorrection { get; set; }
    
    // [规则 4.3] 系统字段 - 创建后不可变
    public DateTime RatedAt { get; init; } = DateTime.UtcNow;
    
    // [规则 2.3, 2.4, 2.5, 2.6] 
    // 导航引用：Photo?, JsonIgnore, ForeignKey, NotNull
    // 严禁 virtual (禁用延迟加载)
    [JsonIgnore]
    [ForeignKey(nameof(PhotoId))]
    [NotNull]
    public Photo? Photo { get; init; }
}
