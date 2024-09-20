using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("api/admin/")]
[Authorize(Roles = "Admin")]
public class AdministrationController : ControllerBase
{
    private readonly IServiceManager _serviceManager;

    public AdministrationController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    [HttpGet("images")]
    public async Task<ActionResult> GetNotApprovedImages()
    {
        var appImagesToApproveAsync = await _serviceManager.AppAdministrationService.GetAppImagesToApproveAsync();
        return Ok(appImagesToApproveAsync);
    }

    [HttpPost("images/{id:int}")]
    public async Task<IActionResult> ChangeImagesApproveStatus(int id, bool approved)
    {
        return Ok();
    }

    public async Task<IActionResult> ParseNewImages()
    {
        return Ok();
    }
}