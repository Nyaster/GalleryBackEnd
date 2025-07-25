using System.Security.Claims;
using Application.Features.Images.GetImageBySearch;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("/api/images")]
public class ImageController(IServiceManager serviceManager, IMediator mediator) : ControllerBase
{
    [Authorize(Roles = "User,Admin")]
    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] AppImageCreationDto request)
    {
        var userClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name);
        if (userClaim == null)
        {
            return Unauthorized();
        }

        var uploadImageAsync = await serviceManager.AppImageService.UploadImageAsync(request, userClaim.Value);
        return CreatedAtRoute("GetImageById", new { id = uploadImageAsync.Id }, uploadImageAsync);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("{id:int}", Name = "GetImageById")]
    public async Task<IActionResult> GetImageById(int id)
    {
        var request = new Application.Features.Images.GetImageById.Command(id);
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("{id:int}/content", Name = "GetImageFileById")]
    public async Task<IActionResult> GetImageById(int id, bool asJpeg = false)
    {
        var request = new Application.Features.Images.GetImageContent.Command(id, asJpeg);
        var result = await mediator.Send(request);
        return result;
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("{id:int}/recommendation", Name = "GetImageRecommendationById")]
    public async Task<IActionResult> GetImageRecommendation(int id)
    {
        var request = new Application.Features.Images.GetImageRecommendation.Command(id);
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("/api/images/search")]
    public async Task<IActionResult> GetOfficialImagesBySearch([FromQuery] List<string> tags, string orderBy, int page,
        int pageSize)
    {
        var searchImageDto = new SearchImageDto(tags, orderBy, page, pageSize, false);
        var request = new Command(searchImageDto);
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("/api/fan/search")]
    public async Task<IActionResult> GetUserMadeImagesBySearch([FromQuery] List<string> tags, string orderBy, int page,
        int pageSize)
    {
        var searchImageDto = new SearchImageDto(tags, orderBy, page, pageSize, true);
        var request = new Command(searchImageDto);
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("/api/tags/suggestions")]
    public async Task<IActionResult> GetTagsSuggestions(string tag)
    {
        var request = new Application.Features.Images.GetTagSuggestion.Command(tag);
        var result = await mediator.Send(request);
        return Ok(result);
    }
}