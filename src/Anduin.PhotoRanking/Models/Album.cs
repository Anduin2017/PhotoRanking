using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Anduin.PhotoRanking.Models;

public class Album
{
    [Key]
    [MaxLength(500)]
    public required string AlbumId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>
    /// 用于目录排名的贝叶斯修正人工均分。它是报表指标，绝不回流到照片分数。
    /// </summary>
    public double AlbumScore { get; set; }

    public double? AverageManualScore { get; set; }

    public int RatedPhotoCount { get; set; }

    /// <summary>
    /// 旧数据库列名。现在只表示相册人工评分覆盖率。
    /// </summary>
    public double KnownRate { get; set; }

    [NotMapped]
    public double RatedRate => KnownRate;

    public double StandardDeviation { get; set; }

    public double? HighestScore { get; set; }

    public double? LowestScore { get; set; }

    public int PhotoCount { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [InverseProperty(nameof(Photo.Album))]
    public IEnumerable<Photo> Photos { get; init; } = new List<Photo>();
}
