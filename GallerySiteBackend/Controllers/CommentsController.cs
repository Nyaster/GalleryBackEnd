using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/comments")]
public sealed class CommentsController(IMediator mediator) : ControllerBase
{
    [HttpDelete("{commentId:int}")]
    public async Task<IActionResult> Delete(int commentId, CancellationToken cancellationToken)
    {
        await mediator.Send(new Application.Features.Images.Comments.DeleteCommentCommand(commentId, false), cancellationToken);
        return NoContent();
    }
}
