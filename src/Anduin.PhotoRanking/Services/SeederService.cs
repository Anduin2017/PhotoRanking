using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

public class SeederService(AppDbContext context, IConfiguration configuration, ILogger<SeederService> logger)
{
    public async Task SeedAsync()
    {
        var photoRootPath = configuration["PhotoRootPath"] ?? throw new InvalidOperationException("PhotoRootPath not configured");

        if (!Directory.Exists(photoRootPath))
        {
            logger.LogWarning("Photo root path does not exist: {Path}", photoRootPath);
            return;
        }

        logger.LogInformation("Starting seeding from: {Path}", photoRootPath);

        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var albumDirectories = Directory.GetDirectories(photoRootPath);

        // 预加载所有现有数据到内存（大幅提升性能）
        var existingAlbumIds = await context.Albums
            .Select(a => a.AlbumId)
            .ToHashSetAsync();

        var existingPhotoPaths = await context.Photos
            .Select(p => p.FilePath)
            .ToHashSetAsync();

        logger.LogInformation("Loaded {AlbumCount} existing albums and {PhotoCount} existing photos",
            existingAlbumIds.Count, existingPhotoPaths.Count);

        var albumsToAdd = new List<Album>();
        var photosToAdd = new List<Photo>();
        var photosSkipped = 0;

        foreach (var albumDir in albumDirectories)
        {
            var albumName = Path.GetFileName(albumDir);
            var albumId = Guid.NewGuid();

            // 使用HashSet快速检查，无需查询数据库
            if (!existingAlbumIds.Contains(albumId))
            {
                var newAlbum = new Album
                {
                    AlbumId = albumId,
                    Name = albumName,
                    AlbumScore = 2.5,
                    KnownRate = 0,
                    StandardDeviation = 0,
                    PhotoCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                albumsToAdd.Add(newAlbum);
                existingAlbumIds.Add(albumId); // 添加到集合中，避免重复
            }

            // 扫描照片
            var photoFiles = Directory.GetFiles(albumDir)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            foreach (var photoFile in photoFiles)
            {
                var fileName = Path.GetFileName(photoFile);
                var relativePath = $"{albumName}/{fileName}";

                // 使用HashSet快速检查，无需查询数据库
                if (!existingPhotoPaths.Contains(relativePath))
                {
                    var newPhoto = new Photo
                    {
                        FilePath = relativePath,
                        AlbumId = albumId,
                        IndependentScore = null,
                        OverallScore = 2.5,
                        Knownness = 0,
                        RatingCount = 0,
                        IsFixed = false,
                        ViewCount = 0,
                        CreatedAt = DateTime.UtcNow
                    };

                    photosToAdd.Add(newPhoto);
                    existingPhotoPaths.Add(relativePath); // 添加到集合中，避免重复
                }
                else
                {
                    photosSkipped++;
                }
            }
        }

        // 批量插入（比逐个插入快得多）
        if (albumsToAdd.Count > 0)
        {
            logger.LogInformation("Adding {Count} new albums...", albumsToAdd.Count);
            await context.Albums.AddRangeAsync(albumsToAdd);
            await context.SaveChangesAsync();
        }

        if (photosToAdd.Count > 0)
        {
            logger.LogInformation("Adding {Count} new photos...", photosToAdd.Count);

            // 分批插入照片（避免一次性插入太多导致内存问题）
            var batchSize = 1000;
            for (int i = 0; i < photosToAdd.Count; i += batchSize)
            {
                var batch = photosToAdd.Skip(i).Take(batchSize).ToList();
                await context.Photos.AddRangeAsync(batch);
                await context.SaveChangesAsync();
                logger.LogInformation("Inserted batch {Current}/{Total} photos",
                    Math.Min(i + batchSize, photosToAdd.Count), photosToAdd.Count);
            }
        }

        logger.LogInformation("Seeding completed. Added: {Albums} albums, {Photos} photos. Skipped: {Skipped} photos",
            albumsToAdd.Count, photosToAdd.Count, photosSkipped);

        // 更新相册统计
        if (albumsToAdd.Count > 0 || photosToAdd.Count > 0)
        {
            await UpdateAlbumStatsAsync();
        }
    }

    public async Task UpdateAlbumStatsAsync()
    {
        var albums = await context.Albums.Include(a => a.Photos).ToListAsync();

        foreach (var album in albums)
        {
            album.PhotoCount = album.Photos.Count();

            var ratedPhotos = album.Photos.Where(p => p.IndependentScore.HasValue).ToList();
            album.KnownRate = album.PhotoCount > 0 ? (double)ratedPhotos.Count / album.PhotoCount : 0;

            // 计算相册分
            if (ratedPhotos.Count > 0)
            {
                var avgRated = ratedPhotos.Average(p => p.IndependentScore!.Value);
                var unratedScore = avgRated - 1;
                var unratedCount = album.PhotoCount - ratedPhotos.Count;

                album.AlbumScore = (ratedPhotos.Sum(p => p.IndependentScore!.Value) + unratedCount * unratedScore) / album.PhotoCount;
            }
            else
            {
                album.AlbumScore = 2.5; // 默认分数
            }

            // 计算标准差、最高分、最低分
            if (album.Photos.Count() > 0)
            {
                var scores = album.Photos.Select(p => p.IndependentScore ?? album.AlbumScore).ToList();
                var mean = scores.Average();
                var variance = scores.Sum(s => Math.Pow(s - mean, 2)) / scores.Count;
                album.StandardDeviation = Math.Sqrt(variance);

                album.HighestScore = scores.Max();
                album.LowestScore = scores.Min();
            }

            album.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Updated statistics for {Count} albums", albums.Count);
    }
}
