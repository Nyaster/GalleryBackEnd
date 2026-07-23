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
    [HttpPut("announcement")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AnnouncementDto>> UpdateAnnouncement(UpdateAnnouncementDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Announcements.UpdateAnnouncementCommand(request), cancellationToken));

    [HttpDelete("announcement")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearAnnouncement(CancellationToken cancellationToken)
    {
        await mediator.Send(new Application.Features.Announcements.ClearAnnouncementCommand(), cancellationToken);
        return NoContent();
    }

    [HttpGet("feedback")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PageableFeedbackDto>> Feedback([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Announcements.GetFeedbackCommand(page, pageSize), cancellationToken));

    [HttpGet("images/pending")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<List<AppImageDto>>> PendingImages([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetPendingImages.Command(page, pageSize), cancellationToken));

    [HttpPatch("images/{id:int}/moderation")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<AppImageDto>> ChangeModeration(int id, ChangeModerationDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.ChangeModeration.Command(id, request.Status, request.AiUsage), cancellationToken));

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

    [HttpGet("tag-changes")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<PageableImageTagChangesDto>> TagChanges([FromQuery] Entities.Models.ImageTagChangeStatus? status = null,
        [FromQuery] int? imageId = null, [FromQuery] int? editedByUserId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetImageTagChanges.Command(status, imageId, editedByUserId, page, pageSize), cancellationToken));

    [HttpPatch("tag-changes/{id:long}/moderation")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<ImageTagChangeDto>> ModerateTagChange(long id, TagChangeModerationDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.ModerateImageTagChange.Command(id, request.Decision!.Value, request.Note), cancellationToken));

    [HttpPost("scrape-runs")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<ScrapeRunDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ScrapeRunDto>> StartScrape(StartScrapeDto request, CancellationToken cancellationToken)
    {
        var run = await mediator.Send(new Application.Features.Administration.StartScrape.Command(request.Mode, request.MaxImages), cancellationToken);
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

    [HttpDelete("comments/{commentId:int}")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> DeleteComment(int commentId, CancellationToken cancellationToken)
    {
        await mediator.Send(new Application.Features.Images.Comments.DeleteCommentCommand(commentId, true), cancellationToken);
        return NoContent();
    }

    [HttpGet("users/{id:int}/comment-restriction")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<CommentRestrictionDto>> GetCommentRestriction(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.CommentRestrictions.GetCommentRestrictionCommand(id), cancellationToken));

    [HttpPut("users/{id:int}/comment-restriction")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<ActionResult<CommentRestrictionDto>> SetCommentRestriction(int id, CommentRestrictionUpdateDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.CommentRestrictions.SetCommentRestrictionCommand(id, request), cancellationToken));

    [HttpDelete("users/{id:int}/comment-restriction")]
    [Authorize(Roles = "Admin,Moderator")]
    public async Task<IActionResult> DeleteCommentRestriction(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new Application.Features.Administration.CommentRestrictions.DeleteCommentRestrictionCommand(id), cancellationToken);
        return NoContent();
    }
}
