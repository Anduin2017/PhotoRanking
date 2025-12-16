using Microsoft.AspNetCore.Mvc;

namespace Anduin.PhotoRanking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImagesController> _logger;
    
    public ImagesController(IConfiguration configuration, ILogger<ImagesController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }
    
    /// <summary>
    /// 提供照片文件（代理本地文件系统）
    /// </summary>
    [HttpGet("{*filePath}")]
    public IActionResult GetImage(string filePath)
    {
        try
        {
            var photoRootPath = _configuration["PhotoRootPath"];
            if (string.IsNullOrEmpty(photoRootPath))
            {
                return StatusCode(500, "PhotoRootPath not configured");
            }
            
            // 安全检查：防止路径穿越攻击
            if (filePath.Contains("..") || Path.IsPathRooted(filePath))
            {
                return BadRequest("Invalid file path");
            }
            
            var fullPath = Path.Combine(photoRootPath, filePath);
            
            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("Image not found: {Path}", fullPath);
                return NotFound();
            }
            
            // 确定MIME类型
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
            
            var fileStream = System.IO.File.OpenRead(fullPath);
            return File(fileStream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving image: {FilePath}", filePath);
            return StatusCode(500, "Error serving image");
        }
    }
}
