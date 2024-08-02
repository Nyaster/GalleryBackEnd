using GallerySiteBackend.Models.Requests;
using GallerySiteBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GallerySiteBackend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("/api/images")]
public class ImageController : ControllerBase
{
    private IAppImageService _appImageService;

    public ImageController(IAppImageService appImageService)
    {
        _appImageService = appImageService;
    }

    [HttpPost]
    public async Task<IActionResult> Upload(AppImageUploadRequest request)
    {
        await _appImageService.UploadImageAsync(request);
        return Created();
    }


    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetImageById(int id)
    {
        var fileBytesAsync = await _appImageService.GetFileBytesAsync(id);
        return fileBytesAsync;
    }
    [Authorize(Roles = "user")]
    [HttpPost("search")]
    public async Task<IActionResult> GetImagesBySearch(GetImageRequest imageRequest)
    {
        var imagesBySearchConditions = await _appImageService.GetImagesBySearchConditions(imageRequest);
        return Ok(imagesBySearchConditions);
        
    }
}