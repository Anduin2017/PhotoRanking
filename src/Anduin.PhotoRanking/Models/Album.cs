using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Anduin.PhotoRanking.Models;

public class Album
{
    // [规则 1.1, 1.2, 1.3] 主键：int, Key, init
    [Key]
    public int Id { get; init; }
    
    // [规则 4.1, 4.2] 必填列：required string, MaxLength
    /// <summary>
    /// 相册ID（文件夹路径）
    /// </summary>
    [MaxLength(500)]
    public required string AlbumId { get; set; }
    
    /// <summary>
    /// 相册名称（文件夹名）
    /// </summary>
    [MaxLength(200)]
    public required string Name { get; set; }
    
    // 相册分数
    public double AlbumScore { get; set; }
    
    // 已知率（打分率）
    public double KnownRate { get; set; }
    
    // 标准差
    public double StandardDeviation { get; set; }
    
    /// <summary>
    /// 最高分。
    /// 若为空，表示相册中还没有任何打分记录。
    /// </summary>
    public double? HighestScore { get; set; }
    
    /// <summary>
    /// 最低分。
    /// 若为空，表示相册中还没有任何打分记录。
    /// </summary>
    public double? LowestScore { get; set; }
    
    // 照片数量
    public int PhotoCount { get; set; }
    
    // [规则 4.3] 系统字段 - 创建后不可变
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    // 更新时间（可变）
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // [规则 3.1, 3.2, 3.3] 
    // 集合：IEnumerable (独裁模式), InverseProperty, new List()
    [InverseProperty(nameof(Photo.Album))]
    public IEnumerable<Photo> Photos { get; init; } = new List<Photo>();
}
