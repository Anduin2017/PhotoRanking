using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Anduin.PhotoRanking.Models;

public class Photo
{
    // [规则 1.1, 1.2, 1.3] 主键：int, Key, init
    [Key]
    public int Id { get; init; }
    
    // [规则 4.1, 4.2] 必填列：required string, MaxLength
    /// <summary>
    /// 照片在文件系统中的相对路径
    /// </summary>
    [MaxLength(1000)]
    public required string FilePath { get; set; }
    
    /// <summary>
    /// 所属相册ID（文件夹路径）
    /// </summary>
    [MaxLength(500)]
    public required string AlbumId { get; set; }
    
    /// <summary>
    /// 独立分数（用户直接打分）。
    /// 若为空，表示该照片尚未被用户评分。
    /// </summary>
    public double? IndependentScore { get; set; }
    
    // 整体分数（独立分与相册分的均值）
    public double OverallScore { get; set; }
    
    // 已知性（基于打分次数和相册打分率）
    public double Knownness { get; set; }
    
    // 用户打分次数
    public int RatingCount { get; set; }
    
    // 是否已固定（最后三次打分相同）
    public bool IsFixed { get; set; }
    
    // 浏览次数
    public int ViewCount { get; set; }
    
    /// <summary>
    /// 最后打分时间。
    /// 若为空，表示该照片从未被评分。
    /// </summary>
    public DateTime? LastRatedAt { get; set; }
    
    // [规则 4.3] 系统字段 - 创建后不可变
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    // [规则 2.3, 2.4, 2.5, 2.6] 
    // 导航引用：Album?, JsonIgnore, ForeignKey, NotNull
    // 注意：AlbumId 是业务键（string），不是数据库外键
    // 关系通过 Fluent API 配置（HasForeignKey + HasPrincipalKey）
    [JsonIgnore]
    [ForeignKey(nameof(AlbumId))]
    [NotNull]
    public Album? Album { get; init; }
    
    // [规则 3.1, 3.2, 3.3] 
    // 集合：IEnumerable (独裁模式), InverseProperty, new List()
    [InverseProperty(nameof(RatingLog.Photo))]
    public IEnumerable<RatingLog> RatingLogs { get; init; } = new List<RatingLog>();
}
