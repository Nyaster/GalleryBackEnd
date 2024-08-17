using Entities.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;

namespace GallerySiteBackend.Presentation;

[ApiController]
[AllowAnonymous]
[Route("/api/images")]
public class ImageController : ControllerBase
{
    private IServiceManager _serviceManager;

    public ImageController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }


    [HttpPost]
    public async Task<IActionResult> Upload(AppImageUploadRequest request)
    {
        await _serviceManager.AppImageService.UploadImageAsync(request);
        return Created();
    }


    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetImageById(int id)
    {
        var fileBytesAsync = await _serviceManager.AppImageService.GetFileBytesAsync(id);
        return fileBytesAsync;
    }

    [Authorize(Roles = "user")]
    [HttpPost("search")]
    public async Task<IActionResult> GetImagesBySearch(GetImageRequest imageRequest)
    {
        var imagesBySearchConditions = await _serviceManager.AppImageService.GetImagesBySearchConditions(imageRequest);
        return Ok(imagesBySearchConditions);
    }
}