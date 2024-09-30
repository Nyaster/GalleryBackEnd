using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("/api/images")]

public class ImageController : ControllerBase
{
    private readonly IServiceManager _serviceManager;

    public ImageController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    [Authorize(Roles = "User,Admin")]
    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] AppImageCreationDto request)
    {
        var value = "admin";
        if (HttpContext.User.Identity.IsAuthenticated)
            value = HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;

        var uploadImageAsync = await _serviceManager.AppImageService.UploadImageAsync(request, value);
        return CreatedAtRoute("GetImageById", new { id = uploadImageAsync.Id }, uploadImageAsync);
    }
    [Authorize(Roles = "User,Admin")]
    [HttpGet("{id:int}", Name = "GetImageById")]
    public async Task<IActionResult> GetImageById(int id)
    {
        var image = await _serviceManager.AppImageService.GetImageByIdAsync(id);
        return Ok(image);
    }
    [Authorize(Roles = "User,Admin")]
    [HttpGet("{id:int}/content", Name = "GetImageFileById")]
    public async Task<IActionResult> GetImageById(int id, bool asJpeg = false)
    {
        var fileBytesAsync = await _serviceManager.AppImageService.GetFileBytesAsync(id, asJpeg);
        return fileBytesAsync;
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("search", Name = "search")]
    public async Task<IActionResult> GetImagesBySearch([FromQuery] List<string> tags, string orderBy, int page,
        int pageSize)
    {
        var searchImageDto = new SearchImageDto(tags, orderBy, page, pageSize);
        var imagesBySearchConditions =
            await _serviceManager.AppImageService.GetImagesBySearchConditions(searchImageDto);
        return Ok(imagesBySearchConditions);
    }



    [Authorize(Roles = "User,Admin")]
    [HttpGet("/api/tags/suggestions")]
    public async Task<IActionResult> GetTagsSuggestions(string tag)
    {
        var tagsSuggestion = await _serviceManager.AppImageService.GetTagsSuggestion(tag);
        return Ok(tagsSuggestion);
    }
}