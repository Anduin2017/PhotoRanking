using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models;

/// <summary>
/// 当前生效的个人化评分模型。模型很小，和数据库一起持久化可确保容器升级后立即可用。
/// </summary>
public class PredictionModel
{
    [Key]
    public int Id { get; init; } = 1;

    [MaxLength(100)]
    public required string Version { get; set; }

    [MaxLength(100)]
    public required string EmbeddingModel { get; set; }

    public required byte[] ModelData { get; set; }

    public DateTime TrainedAt { get; set; }

    /// <summary>
    /// 该模型已经包含到哪一次最终评分。用于容器中断后续跑同一版预测。
    /// </summary>
    public DateTime TrainingRatingWatermark { get; set; }

    public int TrainingPhotoCount { get; set; }

    public int TrainingCandidatePhotoCount { get; set; }

    /// <summary>
    /// 用于视觉覆盖模型的全部有效人工锚点数。回归头可以只学习较新的口味，
    /// 但主动学习仍应知道历史上哪些视觉区域已经被人工覆盖。
    /// </summary>
    public int CoverageTrainingPhotoCount { get; set; }

    public int EnsembleSize { get; set; }

    public int CoverageCentroidCount { get; set; }

    public double? ValidationMeanAbsoluteError { get; set; }
}
