using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;
using Shared.DataTransferObjects;
using IAuthorizationService = Service.Contracts.IAuthorizationService;

namespace GallerySiteBackend.Presentation;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthorizationController : ControllerBase
{
    private IServiceManager _serviceManager;

    public AuthorizationController(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }


    [HttpPost("login")]
    public async Task<IActionResult> Login(AppLoginDto loginRequest)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        var jwtTokenResponse = await _serviceManager.AuthorizationService.LoginAsync(loginRequest);
        return Ok(jwtTokenResponse);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Registration(CreateUserDto registrationRequest)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        await _serviceManager.AuthorizationService.RegisterAsync(registrationRequest);
        return Created();
    }

    [Authorize(AuthenticationSchemes = "IgnoreTokenExpirationScheme")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(AppRefreshhTokenResetDto refreshTokenRequest)
    {
        var token = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var refreshJwtTokenAsync =
            await _serviceManager.AuthorizationService.RefreshJwtTokenAsync(refreshTokenRequest, token);
        return Ok(refreshJwtTokenAsync);
    }
}