using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlbumsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AlbumsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取所有相册
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<object>>> GetAlbums()
    {
        var albums = await _context.Albums
            .OrderByDescending(a => a.AlbumScore)
            .Select(a => new
            {
                a.AlbumId,
                a.Name,
                a.AlbumScore,
                a.KnownRate,
                RatedRate = a.KnownRate,
                a.RatedPhotoCount,
                a.AverageManualScore,
                a.PhotoCount,
                ThumbnailPath = a.Photos
                    .OrderByDescending(p => p.IndependentScore ?? -1)
                    .ThenByDescending(p => p.EstimatedScore ?? -1)
                    .Select(p => p.FilePath)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(albums);
    }

    /// <summary>
    /// 获取相册详情
    /// </summary>
    [HttpGet("{*albumId}")]
    public async Task<ActionResult<object>> GetAlbum(string albumId, [FromQuery] string sortBy = "filename")
    {
        // URL解码albumId以支持包含斜杠的嵌套路径
        albumId = Uri.UnescapeDataString(albumId);

        var album = await _context.Albums
            .Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.AlbumId == albumId);

        if (album == null)
        {
            return NotFound();
        }

        // 根据sortBy参数排序
        var photosList = album.Photos.ToList();
        IEnumerable<Photo> photos = photosList;
        if (sortBy.ToLower() == "estimatedscore")
        {
            photos = photosList.OrderByDescending(p => p.EstimatedScore ?? -1).ThenBy(p => p.FilePath);
        }
        else
        {
            photos = sortBy.ToLower() switch
            {
                "score" or "overallscore" => photos.OrderByDescending(p => p.IndependentScore ?? p.EstimatedScore ?? -1),
                "rated" => photos.OrderByDescending(p => p.IndependentScore.HasValue).ThenByDescending(p => p.IndependentScore).ThenBy(p => p.FilePath),
                "unrated" => photos.OrderBy(p => p.IndependentScore.HasValue).ThenBy(p => p.FilePath),
                "manualscore" or "independentscore" => photos.OrderByDescending(p => p.IndependentScore).ThenByDescending(p => p.EstimatedScore),
                _ => photos.OrderBy(p => p.FilePath)
            };
        }

        return Ok(new
        {
            Album = album,
            Photos = photos.ToList()
        });
    }

    /// <summary>
    /// 获取相册的照片（分页）
    /// </summary>
    [HttpGet("{albumId}/photos")]
    public async Task<ActionResult<object>> GetAlbumPhotos(
        string albumId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string sortBy = "filePath")
    {
        var album = await _context.Albums.FirstOrDefaultAsync(a => a.AlbumId == albumId);
        if (album == null)
        {
            return NotFound();
        }

        var query = _context.Photos.Where(p => p.AlbumId == albumId);

        // 根据sortBy参数排序
        query = sortBy.ToLower() switch
        {
            "estimatedscore" => query.OrderByDescending(p => p.EstimatedScore).ThenBy(p => p.FilePath),
            "manualscore" or "independentscore" => query.OrderByDescending(p => p.IndependentScore).ThenByDescending(p => p.EstimatedScore),
            "score" or "overallscore" => query.OrderByDescending(p => p.IndependentScore ?? p.EstimatedScore),
            "knownness" => query.OrderByDescending(p => p.EstimatedScore), // Legacy alias
            "rated" => query.OrderByDescending(p => p.IndependentScore.HasValue).ThenByDescending(p => p.IndependentScore).ThenBy(p => p.FilePath),
            "unrated" => query.OrderBy(p => p.IndependentScore.HasValue).ThenBy(p => p.FilePath),
            _ => query.OrderBy(p => p.FilePath) // 默认按文件名
        };

        var photos = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 如果按独立分排序且不分页，返回所有照片
        if (sortBy.ToLower() == "independentscore" && page == 1 && pageSize == 30)
        {
            photos = await _context.Photos
                .Where(p => p.AlbumId == albumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.EstimatedScore)
                .ToListAsync();
        }

        return Ok(photos); // 简化返回，只返回照片列表
    }

    /// <summary>
    /// 获取相册分最高的相册（分页）
    /// </summary>
    [HttpGet("top-by-score")]
    public async Task<ActionResult<List<object>>> GetTopByScore([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var albums = await _context.Albums
            .OrderByDescending(a => a.AlbumScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var result = new List<object>();
        foreach (var album in albums)
        {
            var topPhoto = await _context.Photos
                .Where(p => p.AlbumId == album.AlbumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.EstimatedScore)
                .FirstOrDefaultAsync();

            if (topPhoto == null)
            {
                topPhoto = await _context.Photos
                    .Where(p => p.AlbumId == album.AlbumId)
                    .FirstOrDefaultAsync();
            }

            result.Add(new
            {
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.RatedRate,
                album.RatedPhotoCount,
                album.AverageManualScore,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// 获取人工评分覆盖率最高的相册（分页）
    /// </summary>
    [HttpGet("top-by-ratedrate")]
    [HttpGet("top-by-knownrate")] // Legacy API alias
    public async Task<ActionResult<List<object>>> GetTopByRatedRate([FromQuery] int skip = 0, [FromQuery] int take = 5)
    {
        var albums = await _context.Albums
            .OrderByDescending(a => a.KnownRate)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var result = new List<object>();
        foreach (var album in albums)
        {
            var topPhoto = await _context.Photos
                .Where(p => p.AlbumId == album.AlbumId)
                .OrderByDescending(p => p.IndependentScore)
                .ThenByDescending(p => p.EstimatedScore)
                .FirstOrDefaultAsync();

            if (topPhoto == null)
            {
                topPhoto = await _context.Photos
                    .Where(p => p.AlbumId == album.AlbumId)
                    .FirstOrDefaultAsync();
            }

            result.Add(new
            {
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.RatedRate,
                album.RatedPhotoCount,
                album.AverageManualScore,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// 获取相册的去重预览分组
    /// </summary>
    [HttpGet("dedup-preview/{*albumId}")]
    public async Task<ActionResult<object>> DedupPreview(string albumId, [FromServices] DedupService dedupService, [FromQuery] double similarity = 93.0)
    {
        albumId = Uri.UnescapeDataString(albumId);
        var album = await _context.Albums.FirstOrDefaultAsync(a => a.AlbumId == albumId);
        if (album == null)
            return NotFound();

        var photos = await _context.Photos
            .Where(p => p.AlbumId == albumId && p.FeatureVector != null)
            .ToListAsync();

        var groups = dedupService.GetDuplicateGroups(photos, similarity);

        // Map groups to a flat or nested JSON structure
        var result = groups.Select(g => new
        {
            BestPhoto = g.First(),
            Duplicates = g.Skip(1).ToList()
        }).ToList();

        return Ok(result);
    }
}
