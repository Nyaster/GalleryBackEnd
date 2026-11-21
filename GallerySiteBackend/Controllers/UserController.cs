using Application.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/users/me")]
public sealed class UserController(IMediator mediator) : ControllerBase
{
    [HttpPut("content-preferences")]
    public async Task<ActionResult<AppUserDto>> UpdateContentPreferences(
        UpdateContentPreferencesDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateContentPreferencesCommand(request.DisplayAllContent!.Value),
            cancellationToken));

    [HttpGet("images")]
    public async Task<ActionResult<PageableImagesDto>> Images([FromQuery] bool includeHidden = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetMyImages.Command(includeHidden, page, pageSize),
            cancellationToken));

    [HttpGet("liked-images")]
    public async Task<ActionResult<PageableLikedImagesDto>> LikedImages([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] List<string>? aiUsage = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetLikedImages.Command(
            page, pageSize, AiUsageFilterParser.Parse(aiUsage)), cancellationToken));
}