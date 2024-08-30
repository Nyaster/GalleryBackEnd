using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Service;
using Service.Contracts;
using Shared;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("/api/images")]
[AllowAnonymous]
public class ImageController : ControllerBase
{
    private IServiceManager _serviceManager;
    private AppImageParserService _imageParserService;

    public ImageController(IServiceManager serviceManager, AppImageParserService imageParserService)
    {
        _serviceManager = serviceManager;
        _imageParserService = imageParserService;
    }

    [Authorize(Roles = "User,Admin")]
    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] AppImageCreationDto request)
    {
        var value = "admin";
        if (HttpContext.User.Identity.IsAuthenticated)
        {
            value = HttpContext.User.Claims.First(x => x.Type == ClaimTypes.Name).Value;
        }

        var uploadImageAsync = await _serviceManager.AppImageService.UploadImageAsync(request, value);
        return CreatedAtRoute("GetImageById", new { id = uploadImageAsync.Id }, uploadImageAsync);
    }


    [HttpGet("{id:int}", Name = "GetImageById")]
    public async Task<IActionResult> GetImageById(int id)
    {
        var fileBytesAsync = await _serviceManager.AppImageService.GetFileBytesAsync(id);
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
    [HttpGet("test")]
    public async Task<IActionResult> CheckUpdates()
    {
        await _imageParserService.CheckUpdates();
        return Ok();
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("/api/tags/suggestions")]
    public async Task<IActionResult> GetTagsSuggestions(string tag)
    {
        var tagsSuggestion = await _serviceManager.AppImageService.GetTagsSuggestion(tag);
        return Ok(tagsSuggestion);
    }
}