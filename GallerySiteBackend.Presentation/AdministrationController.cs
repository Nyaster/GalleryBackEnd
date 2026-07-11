using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public sealed class AdministrationController(IMediator mediator) : ControllerBase
{
    [HttpGet("images/pending")]
    public async Task<ActionResult<List<AppImageDto>>> PendingImages([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Administration.GetPendingImages.Command(page, pageSize), cancellationToken));

    [HttpPatch("images/{id:int}/moderation")]
    public async Task<ActionResult<AppImageDto>> ChangeModeration(int id, ChangeModerationDto request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.ChangeModeration.Command(id, request.Status), cancellationToken));

    [HttpPost("scrape-runs")]
    [ProducesResponseType<ScrapeRunDto>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<ScrapeRunDto>> StartScrape(StartScrapeDto request, CancellationToken cancellationToken)
    {
        var run = await mediator.Send(new Application.Features.Administration.StartScrape.Command(request.Mode), cancellationToken);
        return AcceptedAtAction(nameof(GetScrape), new { id = run.Id }, run);
    }

    [HttpGet("scrape-runs/{id:guid}")]
    public async Task<ActionResult<ScrapeRunDto>> GetScrape(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new Application.Features.Administration.GetScrapeRun.Command(id), cancellationToken));
}
