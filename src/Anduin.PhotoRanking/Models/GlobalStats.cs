namespace Anduin.PhotoRanking.Models;

public class GlobalStats
{
    public int WaitingCount { get; set; }
    public int RatedCount { get; set; }
    public int FullyUnknownAlbumCount { get; set; }
    public int FullyKnownAlbumCount { get; set; }
    public int FullyUnratedAlbumCount { get; set; }
    public int FullyRatedAlbumCount { get; set; }
    public Dictionary<int, int> ScoreDistribution { get; set; } = new();
    public double AveragePhotosPerAlbum { get; set; }
    public double AverageAlbumKnownRate { get; set; }
    public double OverallAverageScore { get; set; }
    public double AverageAlbumRatedRate { get; set; }
    public double ManualAverageScore { get; set; }
    public int IndexedPhotoCount { get; set; }
    public int TotalPhotoCount { get; set; }
    public int PredictionEvaluationCount { get; set; }
    public double? PredictionMeanAbsoluteError { get; set; }
    public double? PredictionWithinOneRate { get; set; }
    public string? ActivePredictionModelVersion { get; set; }
    public DateTime? ActivePredictionModelTrainedAt { get; set; }
    public DateTime? ActivePredictionModelRatingWatermark { get; set; }
    public int? ActivePredictionModelTrainingPhotoCount { get; set; }
    public int? ActivePredictionCoverageTrainingPhotoCount { get; set; }
    public double? ActivePredictionModelValidationMae { get; set; }
    public int? ActivePredictionModelEnsembleSize { get; set; }
    public int PredictionReadyCount { get; set; }
    public int ActiveLearningReadyCount { get; set; }
    public double? AveragePredictionUncertainty { get; set; }
    public double? AveragePredictionNovelty { get; set; }
    public int? ActivePredictionCoverageCentroidCount { get; set; }
}
