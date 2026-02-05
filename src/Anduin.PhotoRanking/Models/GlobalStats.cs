namespace Anduin.PhotoRanking.Models;

public class GlobalStats
{
    public int WaitingCount { get; set; }
    public int RatedCount { get; set; }
    public int FullyUnknownAlbumCount { get; set; }
    public int FullyKnownAlbumCount { get; set; }
    public Dictionary<int, int> ScoreDistribution { get; set; } = new();
    public double AveragePhotosPerAlbum { get; set; }
    public double AverageAlbumKnownRate { get; set; }
    public double OverallAverageScore { get; set; }
    public int IndexedPhotoCount { get; set; }
    public int TotalPhotoCount { get; set; }
}
