using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Canon;
using System.Diagnostics;

namespace Anduin.PhotoRanking.Services;

public class SeederService(
    AppDbContext context, 
    IConfiguration configuration, 
    ILogger<SeederService> logger,
    ImageAnalysisService imageAnalysis,
    ScoringService scoringService,
    IServiceScopeFactory scopeFactory,
    CanonPool canonPool)
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
        var foundPhotoPaths = new HashSet<string>();
        var foundAlbumIds = new HashSet<string>();
        var photosSkipped = 0;

        // 递归扫描所有目录
        ScanDirectoryRecursive(
            photoRootPath,
            photoRootPath,
            supportedExtensions,
            existingAlbumIds,
            existingPhotoPaths,
            albumsToAdd,
            photosToAdd,
            ref photosSkipped,
            foundPhotoPaths,
            foundAlbumIds);

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

        // 清理数据库中已不存在的文件
        var allPhotos = await context.Photos.ToListAsync();
        var photosToRemove = allPhotos
            .Where(p => !foundPhotoPaths.Contains(p.FilePath))
            .ToList();

        if (photosToRemove.Count > 0)
        {
            logger.LogInformation("Removing {Count} photos that no longer exist on disk...", photosToRemove.Count);
            context.Photos.RemoveRange(photosToRemove);
            await context.SaveChangesAsync();
        }

        var allAlbums = await context.Albums.ToListAsync();
        var albumsToRemove = allAlbums
            .Where(a => !foundAlbumIds.Contains(a.AlbumId))
            .ToList();

        if (albumsToRemove.Count > 0)
        {
            logger.LogInformation("Removing {Count} albums that no longer exist on disk or have no photos...", albumsToRemove.Count);
            context.Albums.RemoveRange(albumsToRemove);
            await context.SaveChangesAsync();
        }

        logger.LogInformation("Seeding completed. Added: {Albums} albums, {Photos} photos. Removed: {RemovedPhotos} photos, {RemovedAlbums} albums. Skipped: {Skipped} photos",
            albumsToAdd.Count, photosToAdd.Count, photosToRemove.Count, albumsToRemove.Count, photosSkipped);

        // Update metadata for existing photos if missing
        // Fetch IDs first to avoid infinite loops if update fails and to manage memory
        var allIdsToUpdate = await context.Photos
            .Where(p => p.FileSize == 0 || p.FeatureVector == null)
            .Select(p => p.Id)
            .ToListAsync();

        if (allIdsToUpdate.Count > 0)
        {
            logger.LogInformation("Updating metadata and vectors for {Count} photos using 16 concurrency...", allIdsToUpdate.Count);
            var sw = Stopwatch.StartNew();
            var processedCount = 0;
            const int batchSize = 1000;

            for (int i = 0; i < allIdsToUpdate.Count; i += batchSize)
            {
                var batchIds = allIdsToUpdate.Skip(i).Take(batchSize).ToList();
                foreach (var photoId in batchIds)
                {
                    canonPool.RegisterNewTaskToPool(async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var photo = await dbContext.Photos.FindAsync(photoId);
                        if (photo != null)
                        {
                            var fullPath = Path.Combine(photoRootPath, photo.FilePath);
                            if (File.Exists(fullPath))
                            {
                                var fi = new FileInfo(fullPath);
                                if (photo.FileSize == 0)
                                {
                                    photo.FileSize = fi.Length;
                                    photo.LastModified = fi.LastWriteTimeUtc;
                                }

                                if (photo.FeatureVector == null)
                                {
                                    photo.FeatureVector = imageAnalysis.GenerateVector(fullPath);
                                }
                            }
                            await dbContext.SaveChangesAsync();
                        }

                        var current = Interlocked.Increment(ref processedCount);
                        if (current % 500 == 0 || current == allIdsToUpdate.Count)
                        {
                            var speed = current / sw.Elapsed.TotalSeconds;
                            logger.LogInformation("Processed {Current}/{Total} photos ({Percentage:P1}). Speed: {Speed:F2} photos/s. Elapsed: {Elapsed}",
                                current, allIdsToUpdate.Count, (double)current / allIdsToUpdate.Count, speed, sw.Elapsed);
                        }
                    });
                }
                await canonPool.RunAllTasksInPoolAsync(16);
            }
            
            sw.Stop();
            logger.LogInformation("Metadata and vectors update completed in {Elapsed}. Average speed: {Speed:F2} photos/s", 
                sw.Elapsed, allIdsToUpdate.Count / sw.Elapsed.TotalSeconds);
        }

        // 更新相册统计 (Always update to ensure score calculation rules are applied)
        await UpdateAlbumStatsAsync();
    }

    /// <summary>
    /// 递归扫描目录，只将包含照片的目录作为相册
    /// </summary>
    private void ScanDirectoryRecursive(
        string currentDir,
        string rootPath,
        string[] supportedExtensions,
        HashSet<string> existingAlbumIds,
        HashSet<string> existingPhotoPaths,
        List<Album> albumsToAdd,
        List<Photo> photosToAdd,
        ref int photosSkipped,
        HashSet<string> foundPhotoPaths,
        HashSet<string> foundAlbumIds)
    {
        // 获取当前目录下的所有照片（不递归）
        var photoFiles = Directory.GetFiles(currentDir)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        // 如果当前目录有照片，则将其作为一个相册
        if (photoFiles.Count > 0)
        {
            var albumId = Path.GetRelativePath(rootPath, currentDir).Replace(Path.DirectorySeparatorChar, '/');
            var albumName = Path.GetFileName(currentDir);
            
            // 标记相册已找到
            foundAlbumIds.Add(albumId);

            // 如果相册不存在，创建它
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

            // 处理当前相册的所有照片
            foreach (var photoFile in photoFiles)
            {
                // 照片的相对路径是相对于根目录的
                var relativePath = Path.GetRelativePath(rootPath, photoFile).Replace(Path.DirectorySeparatorChar, '/');
                
                // 标记照片已找到
                foundPhotoPaths.Add(relativePath);

                // 使用HashSet快速检查，无需查询数据库
                if (!existingPhotoPaths.Contains(relativePath))
                {
                    var fileInfo = new FileInfo(photoFile);
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
                        CreatedAt = DateTime.UtcNow,
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        FeatureVector = null
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

        // 递归处理所有子目录
        var subdirectories = Directory.GetDirectories(currentDir);
        foreach (var subdir in subdirectories)
        {
            ScanDirectoryRecursive(
                subdir,
                rootPath,
                supportedExtensions,
                existingAlbumIds,
                existingPhotoPaths,
                albumsToAdd,
                photosToAdd,
                ref photosSkipped,
                foundPhotoPaths,
                foundAlbumIds);
        }
    }

    public async Task UpdateAlbumStatsAsync()
    {
        await scoringService.RebuildAllAlbumStatsAsync();
        logger.LogInformation("Rebuilt album reporting statistics from final manual scores");
    }
}
