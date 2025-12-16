using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
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
    public async Task<ActionResult<List<Album>>> GetAlbums()
    {
        var albums = await _context.Albums
            .OrderByDescending(a => a.AlbumScore)
            .ToListAsync();
        
        return Ok(albums);
    }
    
    /// <summary>
    /// 获取相册详情
    /// </summary>
    [HttpGet("{albumId}")]
    public async Task<ActionResult<object>> GetAlbum(string albumId)
    {
        var album = await _context.Albums
            .Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.AlbumId == albumId);
        
        if (album == null)
        {
            return NotFound();
        }
        
        // 按文件名排序
        var photos = album.Photos
            .OrderBy(p => p.FilePath)
            .ToList();
        
        return Ok(new
        {
            Album = album,
            Photos = photos
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
            "independentscore" => query.OrderByDescending(p => p.IndependentScore).ThenByDescending(p => p.OverallScore),
            "overallscore" => query.OrderByDescending(p => p.OverallScore),
            "knownness" => query.OrderByDescending(p => p.Knownness),
            _ => query.OrderBy(p => p.FilePath) // 默认按文件名
        };
        
        var totalCount = await _context.Photos.CountAsync(p => p.AlbumId == albumId);
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
                .ThenByDescending(p => p.OverallScore)
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
                .ThenByDescending(p => p.OverallScore)
                .FirstOrDefaultAsync();
            
            result.Add(new
            {
                album.Id,
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }
        
        return Ok(result);
    }
    
    /// <summary>
    /// 获取已知率最高的相册（分页）
    /// </summary>
    [HttpGet("top-by-knownrate")]
    public async Task<ActionResult<List<object>>> GetTopByKnownRate([FromQuery] int skip = 0, [FromQuery] int take = 5)
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
                .ThenByDescending(p => p.OverallScore)
                .FirstOrDefaultAsync();
            
            result.Add(new
            {
                album.Id,
                album.AlbumId,
                album.Name,
                album.KnownRate,
                album.AlbumScore,
                album.PhotoCount,
                ThumbnailPath = topPhoto?.FilePath
            });
        }
        
        return Ok(result);
    }
}
