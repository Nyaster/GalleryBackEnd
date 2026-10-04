using Application.Features.Discovery;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/discovery")]
public sealed class DiscoveryController(IMediator mediator) : ControllerBase
{
    [HttpGet("{collection}")]
    [ProducesResponseType<PageableDiscoveryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PageableDiscoveryDto>> Get(string collection, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] string? date = null,
        [FromQuery] List<string>? aiUsage = null, CancellationToken cancellationToken = default)
    {
        // Model binding turns empty strings into null; a supplied empty date is still invalid.
        var requestedDate = date ?? (Request.Query.ContainsKey("date") ? string.Empty : null);
        var result = await mediator.Send(new GetDiscoveryCommand(collection, requestedDate, page, pageSize,
            AiUsageFilterParser.Parse(aiUsage)), cancellationToken);
        Response.Headers.CacheControl = "private";
        return Ok(result);
    }
}