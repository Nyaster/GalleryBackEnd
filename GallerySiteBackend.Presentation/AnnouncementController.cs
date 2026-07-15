using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/announcement")]
public sealed class AnnouncementController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AnnouncementDto>> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Announcements.GetAnnouncementCommand(), cancellationToken));
}
