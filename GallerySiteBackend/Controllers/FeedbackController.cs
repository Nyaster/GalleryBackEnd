using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/feedback")]
public sealed class FeedbackController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("feedback-write")]
    [ProducesResponseType<FeedbackDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FeedbackDto>> Create(CreateFeedbackDto request, CancellationToken cancellationToken)
    {
        var feedback = await mediator.Send(new Application.Features.Announcements.CreateFeedbackCommand(request), cancellationToken);
        return Created($"/api/feedback/{feedback.Id}", feedback);
    }
}
