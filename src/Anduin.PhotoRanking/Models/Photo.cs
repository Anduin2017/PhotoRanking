using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Anduin.PhotoRanking.Models;

public class Photo
{
    [Key]
    public int Id { get; init; }

    /// <summary>
    /// 照片在文件系统中的相对路径
    /// </summary>
    [MaxLength(1000)]
    public required string FilePath { get; set; }

    /// <summary>
    /// 用户对这张照片的最终评分。
    /// 重复评分只表示纠错；任何历史评分、评分次数都不得影响此值。
    /// 保留旧属性名和数据库列名以兼容现有数据库。
    /// </summary>
    public double? IndependentScore { get; set; }

    /// <summary>
    /// 旧版综合分，仅为数据库和旧客户端兼容保留。
    /// 新代码不得用它进行排序、推荐或训练。
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// 旧版已知性，仅为数据库兼容保留。
    /// </summary>
    public double Knownness { get; set; }

    /// <summary>
    /// 旧版打分次数，仅为数据库兼容保留，不再维护。
    /// </summary>
    public int RatingCount { get; set; }

    /// <summary>
    /// 旧版固定标记，仅为数据库兼容保留。
    /// </summary>
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

    /// <summary>
    /// 所属相册ID（目录相对路径）
    /// </summary>
    [MaxLength(500)]
    public required string AlbumId { get; set; }

    // [规则 2.3, 2.4, 2.5, 2.6]
    // 导航引用：Album?, JsonIgnore, ForeignKey, NotNull
    // AlbumId 是string类型的外键，使用目录相对路径作为自然键
    [JsonIgnore]
    [ForeignKey(nameof(AlbumId))]
    [NotNull]
    public Album? Album { get; init; }

    // [规则 3.1, 3.2, 3.3]
    // 集合：IEnumerable (独裁模式), InverseProperty, new List()
    [InverseProperty(nameof(RatingLog.Photo))]
    public IEnumerable<RatingLog> RatingLogs { get; init; } = new List<RatingLog>();

    public long FileSize { get; set; }

    /// <summary>
    /// 图像特征向量。它只在服务端用于预测，绝不发送给浏览器。
    /// </summary>
    [JsonIgnore]
    public byte[]? FeatureVector { get; set; }

    [NotMapped]
    public double? Similarity { get; set; }

    public double? EstimatedScore { get; set; }

    public DateTime? EstimatedScoreUpdatedAt { get; set; }

    [MaxLength(100)]
    public string? EstimatedScoreModelVersion { get; set; }

    /// <summary>
    /// 同一训练集的多个采样模型对这张图的分歧程度。
    /// 它是主动学习元数据，不是第三种照片分数。
    /// </summary>
    public double? PredictionUncertainty { get; set; }

    /// <summary>
    /// 图像距离已评分视觉覆盖中心的程度，用于寻找缺少锚点的内容区域。
    /// 它是主动学习元数据，不是照片分数。
    /// </summary>
    public double? PredictionNovelty { get; set; }

    /// <summary>
    /// 新 API 名称：最终人工分。旧 IndependentScore 字段继续输出以兼容旧客户端。
    /// </summary>
    [NotMapped]
    public double? ManualScore
    {
        get => IndependentScore;
        set => IndependentScore = value;
    }

    /// <summary>
    /// 新 API 名称：AI 在人工评分前给出的预测分。
    /// </summary>
    [NotMapped]
    public double? PredictedScore
    {
        get => EstimatedScore;
        set => EstimatedScore = value;
    }

    /// <summary>
    /// 页面统一展示分。它不是第三种分数，也不持久化。
    /// </summary>
    [NotMapped]
    public double? DisplayScore => IndependentScore ?? EstimatedScore;

    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
