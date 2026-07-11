using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/users/me")]
public sealed class UserController(IMediator mediator) : ControllerBase
{
    [HttpGet("images")]
    public async Task<ActionResult<List<AppImageDto>>> Images(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Images.GetMyImages.Command(), cancellationToken));
}
