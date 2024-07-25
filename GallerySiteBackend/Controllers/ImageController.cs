using GallerySiteBackend.Models.Requests;
using GallerySiteBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace GallerySiteBackend.Controllers;

[ApiController]
[Route("/api/images")]
public class ImageController : ControllerBase
{
    private IAppImageService _appImageService;

    public ImageController(IAppImageService appImageService)
    {
        _appImageService = appImageService;
    }

    public async Task<IActionResult> Upload(AppImageUploadRequest request)
    {
        await _appImageService.UploadImageAsync(request);
        return Created();
    }
}