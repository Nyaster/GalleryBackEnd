using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;

namespace GallerySiteBackend.Presentation;

[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("images")]
    public async Task<IActionResult> GetUploadedImages()
    {
        var identityName = HttpContext.User.Identity?.Name;
        var uploadedImagesAsync = await _userService.GetUploadedImagesAsync(identityName);
        return Ok(uploadedImagesAsync);
    }
}