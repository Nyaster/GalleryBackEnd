using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;

namespace GallerySiteBackend.Presentation;

[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IServiceManager _serviceManager;

    public UserController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    [HttpGet("images")]
    public async Task<IActionResult> GetUploadedImages()
    {
        var identityName = HttpContext.User.Identity.Name;
        var uploadedImagesAsync = await _serviceManager.UserService.GetUploadedImagesAsync(identityName);
        return Ok(uploadedImagesAsync);
    }
}