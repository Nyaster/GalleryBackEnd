using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/content-visibility-rules")]
public sealed class ContentVisibilityRulesController(IRepositoryManager repositories) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ContentVisibilityRuleDto>>> List(CancellationToken cancellationToken)
        => Ok(await repositories.ContentVisibilityRules.ListAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContentVisibilityRuleDto>> Get(int id, CancellationToken cancellationToken)
        => Ok(await repositories.ContentVisibilityRules.GetAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<ContentVisibilityRuleDto>> Create(
        SaveContentVisibilityRuleDto request, CancellationToken cancellationToken)
    {
        var rule = await repositories.ContentVisibilityRules.SaveAsync(null, request.TagIds, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = rule.Id }, rule);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContentVisibilityRuleDto>> Update(
        int id, SaveContentVisibilityRuleDto request, CancellationToken cancellationToken)
        => Ok(await repositories.ContentVisibilityRules.SaveAsync(id, request.TagIds, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await repositories.ContentVisibilityRules.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
