using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize]
[Route("api/tags")]
public sealed class TagsController(IMediator mediator) : ControllerBase
{
    [HttpGet("suggestions")]
    public async Task<ActionResult<List<TagsDto>>> Suggestions([FromQuery] string query, [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new Application.Features.Images.GetTagSuggestion.Command(query, limit), cancellationToken));
}
