using Entities.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/rankings")]
public sealed class RankingsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{period}")]
    public async Task<ActionResult<PageableRankingsDto>> Get(RankingPeriod period, [FromQuery] DateTimeOffset? periodStartUtc,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Rankings.GetRankingsCommand(period, periodStartUtc, page, pageSize), cancellationToken));
}
