using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/admin")]
public sealed class AdministrationController(IMediator mediator) : ControllerBase
{
    [HttpGet("images/pending")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<AppImageDto>>> PendingImages([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetPendingImages.Command(page, pageSize), cancellationToken));

    [HttpPatch("images/{id:int}/moderation")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<AppImageDto>> ChangeModeration(int id, ChangeModerationDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.ChangeModeration.Command(id, request.Status), cancellationToken));

    [HttpGet("images/hidden")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<AppImageDto>>> HiddenImages([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetHiddenImages.Command(page, pageSize), cancellationToken));

    [HttpGet("tags")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<PageableTagsDto>> Tags([FromQuery] Entities.Models.TagModerationStatus status = Entities.Models.TagModerationStatus.Pending,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetTags.Command(status, page, pageSize), cancellationToken));

    [HttpPatch("tags/{id:int}/moderation")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<AdminTagDto>> ChangeTagModeration(int id, TagModerationDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.ChangeTagModeration.Command(id, request.Status), cancellationToken));

    [HttpPost("scrape-runs")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<ScrapeRunDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ScrapeRunDto>> StartScrape(StartScrapeDto request, CancellationToken cancellationToken)
    {
        var run = await mediator.Send(new Application.Features.Administration.StartScrape.Command(request.Mode), cancellationToken);
        return AcceptedAtAction(nameof(GetScrape), new { id = run.Id }, run);
    }

    [HttpGet("scrape-runs/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ScrapeRunDto>> GetScrape(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.GetScrapeRun.Command(id), cancellationToken));

    [HttpGet("users/{id:int}/upload-permission")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UploadPermissionDto>> GetUploadPermission(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.GetUserUploadPermission.Command(id), cancellationToken));

    [HttpPut("users/{id:int}/upload-permission")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UploadPermissionDto>> UpdateUploadPermission(int id, UpdateUploadPermissionDto request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.UpdateUserUploadPermission.Command(id, request.CanUploadImages), cancellationToken));
}
