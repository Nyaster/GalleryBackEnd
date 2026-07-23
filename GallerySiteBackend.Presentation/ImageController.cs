using Application.Features.Images.GetImageContent;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/images")]
public sealed class ImageController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AppImageDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AppImageDto>> Upload([FromForm] AppImageCreationDto request,
        CancellationToken cancellationToken)
    {
        var image = await mediator.Send(new Application.Features.Images.UploadImage.Command(request),
            cancellationToken);
        return CreatedAtRoute("GetImage", new { id = image.Id }, image);
    }

    [HttpGet("{id:int}", Name = "GetImage")]
    public async Task<ActionResult<AppImageDto>> Get(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageById.Command(id), cancellationToken));

    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> GetContent(int id, [FromQuery] string format = "original",
        CancellationToken cancellationToken = default)
    {
        var contentFormat = format.ToLowerInvariant() switch
        {
            "original" => ImageContentFormat.Original,
            "jpeg" => ImageContentFormat.Jpeg,
            "preview" => ImageContentFormat.Preview,
            _ => (ImageContentFormat?)null
        };
        if (contentFormat is null)
            return BadRequest("format must be 'original', 'jpeg', or 'preview'.");

        var content = await mediator.Send(new Command(id, contentFormat.Value), cancellationToken);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content.Stream, content.ContentType);
    }

    [HttpGet]
    public async Task<ActionResult<PageableImagesDto>> Search([FromQuery] List<string>? tags,
        [FromQuery] ImageKind kind = ImageKind.All,
        [FromQuery] ImageSort sort = ImageSort.Newest, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] List<string>? aiUsage = null,
        [FromQuery] string? randomSeed = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageBySearch.Command(
                new SearchImageDto(tags, kind, sort, page, pageSize, AiUsageFilterParser.Parse(aiUsage), randomSeed)),
            cancellationToken));

    [HttpGet("{id:int}/recommendations")]
    public async Task<ActionResult<PageableRecommendationsDto>> Recommendations(int id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] List<string>? aiUsage = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageRecommendation.Command(
            id, page, pageSize, AiUsageFilterParser.Parse(aiUsage)), cancellationToken));

    [HttpPut("{id:int}/tags")]
    [ProducesResponseType<ImageTagChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ImageTagChangeDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ImageTagChangeDto>> ReplaceTags(int id, ReplaceImageTagsDto request,
        CancellationToken cancellationToken)
    {
        var change = await mediator.Send(new Application.Features.Images.ReplaceImageTags.Command(id, request),
            cancellationToken);
        return change.Status == Entities.Models.ImageTagChangeStatus.Pending
            ? AcceptedAtAction(nameof(TagChanges), new { id }, change)
            : Ok(change);
    }

    [HttpGet("{id:int}/tag-changes")]
    public async Task<ActionResult<PageableImageTagChangesDto>> TagChanges(int id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageTagChanges.Command(id, page, pageSize),
            cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Hide(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new Application.Features.Images.HideImage.Command(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<ActionResult<AppImageDto>> Restore(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.RestoreImage.Command(id), cancellationToken));

    [HttpPost("{id:int}/publish")]
    public async Task<ActionResult<AppImageDto>> Publish(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.PublishImage.Command(id), cancellationToken));

    [HttpGet("{imageId:int}/comments")]
    public async Task<ActionResult<PageableCommentsDto>> Comments(int imageId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.Comments.GetCommentsCommand(imageId, page, pageSize),
            cancellationToken));

    [HttpPost("{imageId:int}/comments")]
    [EnableRateLimiting("comment-write")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> CreateComment(int imageId, CreateCommentDto request,
        CancellationToken cancellationToken)
    {
        var comment =
            await mediator.Send(new Application.Features.Images.Comments.CreateCommentCommand(imageId, request),
                cancellationToken);
        return Created($"/api/comments/{comment.Id}", comment);
    }

    [HttpPut("{imageId:int}/likes/me")]
    public async Task<ActionResult<LikeSummaryDto>> Like(int imageId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.Likes.SetLikeCommand(imageId, true),
            cancellationToken));

    [HttpDelete("{imageId:int}/likes/me")]
    public async Task<ActionResult<LikeSummaryDto>> Unlike(int imageId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.Likes.SetLikeCommand(imageId, false),
            cancellationToken));
}
