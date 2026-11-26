using Application.Features.Images.GetImageContent;
using Application.Features.Images.Views;
using Entities.Exceptions;
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
    [HttpGet("for-me")]
    [EnableRateLimiting("for-me")]
    [ProducesResponseType<ForMeFeedDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ForMeFeedDto>> ForMe([FromQuery] int pageSize = 20,
        [FromQuery] List<string>? aiUsage = null, [FromQuery] List<string>? excludedTags = null,
        [FromQuery] string? cursor = null, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Vary = "Authorization";
        if (Request.Query.ContainsKey("cursor"))
        {
            if (string.IsNullOrWhiteSpace(cursor) || Request.Query.ContainsKey("pageSize") ||
                Request.Query.ContainsKey("aiUsage") || Request.Query.ContainsKey("excludedTags"))
                throw new Entities.Exceptions.Base400BadRequestException(
                    "A continuation requires only a nonempty cursor; filters and pageSize cannot change.");
        }

        return Ok(await mediator.Send(new Application.Features.Images.GetForMe.Command(pageSize,
            AiUsageFilterParser.Parse(aiUsage), excludedTags, cursor), cancellationToken));
    }

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

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
    [HttpGet("{id:int}", Name = "GetImage")]
    public async Task<ActionResult<AppImageDto>> Get(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageById.Command(id), cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
    [HttpPost("{id:int}/views")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordView(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RecordImageView(id), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
    [HttpGet("{id:int}/views/daily")]
    [ProducesResponseType<IReadOnlyList<ImageViewDay>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ImageViewDay>>> GetDailyViews(int id, [FromQuery] string? from,
        [FromQuery] string? through, CancellationToken cancellationToken)
    {
        if ((Request.Query.TryGetValue("from", out var fromValues) && (fromValues.Count != 1 || from is null)) ||
            (Request.Query.TryGetValue("through", out var throughValues) &&
             (throughValues.Count != 1 || through is null)))
            throw new Base400BadRequestException("Supply one nonempty value for each date bound.");

        return Ok(await mediator.Send(new GetDailyImageViews(id, from, through), cancellationToken));
    }

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
    [HttpGet("{id:int}/content")]
    public Task<IActionResult> GetContent(int id, [FromQuery] string format = "original",
        CancellationToken cancellationToken = default) => ReadContent(id, format, false, cancellationToken);

    [HttpGet("/api/admin/images/{id:int}/content")]
    [Authorize(Policy = "images.readReviewContent")]
    public Task<IActionResult> GetModerationContent(int id, [FromQuery] string format = "original",
        CancellationToken cancellationToken = default) => ReadContent(id, format, true, cancellationToken);

    private async Task<IActionResult> ReadContent(int id, string format, bool forModeration,
        CancellationToken cancellationToken)
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

        var content = await mediator.Send(new Command(id, contentFormat.Value, forModeration), cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        return File(content.Stream, content.ContentType);
    }

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
    [HttpGet]
    public async Task<ActionResult<PageableImagesDto>> Search([FromQuery] List<string>? tags,
        [FromQuery] ImageKind kind = ImageKind.All,
        [FromQuery] ImageSort sort = ImageSort.Newest, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] List<string>? aiUsage = null,
        [FromQuery] string? randomSeed = null,
        [FromQuery] List<string>? excludedTags = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageBySearch.Command(
                new SearchImageDto(tags, kind, sort, page, pageSize, AiUsageFilterParser.Parse(aiUsage), randomSeed,
                    excludedTags)),
            cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
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
    [ProducesResponseType<AppImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<AppImageDto>> Restore(int id, CancellationToken cancellationToken)
    {
        var image = await mediator.Send(new Application.Features.Images.RestoreImage.Command(id), cancellationToken);
        return image is null ? NoContent() : Ok(image);
    }

    [HttpPost("{id:int}/publish")]
    [ProducesResponseType<AppImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<AppImageDto>> Publish(int id, CancellationToken cancellationToken)
    {
        var image = await mediator.Send(new Application.Features.Images.PublishImage.Command(id), cancellationToken);
        return image is null ? NoContent() : Ok(image);
    }

    [AllowAnonymous]
    [EnableRateLimiting("guest-read")]
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