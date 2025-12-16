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
    
    // 打分（0-5）
    public int Score { get; set; }
    
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
