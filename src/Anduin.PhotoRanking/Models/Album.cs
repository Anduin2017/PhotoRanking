using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Anduin.PhotoRanking.Models;

public class Album
{
    [Key]
    public required Guid AlbumId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    public double AlbumScore { get; set; }

    public double KnownRate { get; set; }

    public double StandardDeviation { get; set; }

    public double? HighestScore { get; set; }

    public double? LowestScore { get; set; }

    public int PhotoCount { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [InverseProperty(nameof(Photo.Album))]
    public IEnumerable<Photo> Photos { get; init; } = new List<Photo>();
}
