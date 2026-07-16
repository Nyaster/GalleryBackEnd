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
    public async Task<ActionResult<AppImageDto>> Upload([FromForm] AppImageCreationDto request, CancellationToken cancellationToken)
    {
        var image = await mediator.Send(new Application.Features.Images.UploadImage.Command(request), cancellationToken);
        return CreatedAtRoute("GetImage", new { id = image.Id }, image);
    }

    [HttpGet("{id:int}", Name = "GetImage")]
    public async Task<ActionResult<AppImageDto>> Get(int id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageById.Command(id), cancellationToken));

    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> GetContent(int id, [FromQuery] string format = "original", CancellationToken cancellationToken = default)
    {
        if (!string.Equals(format, "original", StringComparison.OrdinalIgnoreCase) && !string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase))
            return BadRequest("format must be 'original' or 'jpeg'.");
        var content = await mediator.Send(new Command(id, string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)), cancellationToken);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content.Stream, content.ContentType);
    }

    [HttpGet]
    public async Task<ActionResult<PageableImagesDto>> Search([FromQuery] List<string>? tags, [FromQuery] ImageKind kind = ImageKind.All,
        [FromQuery] ImageSort sort = ImageSort.Newest, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageBySearch.Command(new SearchImageDto(tags, kind, sort, page, pageSize)), cancellationToken));

    [HttpGet("{id:int}/recommendations")]
    public async Task<ActionResult<List<AppImageDto>>> Recommendations(int id, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetImageRecommendation.Command(id, limit), cancellationToken));

    [HttpPut("{id:int}/tags")]
    public async Task<ActionResult<AppImageDto>> ReplaceTags(int id, ReplaceImageTagsDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.ReplaceImageTags.Command(id, request), cancellationToken));

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
    public async Task<ActionResult<PageableCommentsDto>> Comments(int imageId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.Comments.GetCommentsCommand(imageId, page, pageSize), cancellationToken));

    [HttpPost("{imageId:int}/comments")]
    [EnableRateLimiting("comment-write")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> CreateComment(int imageId, CreateCommentDto request, CancellationToken cancellationToken)
    {
        var comment = await mediator.Send(new Application.Features.Images.Comments.CreateCommentCommand(imageId, request), cancellationToken);
        return Created($"/api/comments/{comment.Id}", comment);
    }

    [HttpPut("{imageId:int}/likes/me")]
    public async Task<ActionResult<LikeSummaryDto>> Like(int imageId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.Likes.SetLikeCommand(imageId, true), cancellationToken));

    [HttpDelete("{imageId:int}/likes/me")]
    public async Task<ActionResult<LikeSummaryDto>> Unlike(int imageId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.Likes.SetLikeCommand(imageId, false), cancellationToken));
}
