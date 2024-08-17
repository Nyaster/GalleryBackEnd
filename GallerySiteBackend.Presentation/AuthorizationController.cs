using Entities.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;
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
    public async Task<IActionResult> Login(AppLoginRequest loginRequest)
    {
        var jwtTokenResponse = await _serviceManager.AuthorizationService.LoginAsync(loginRequest);
        return Ok(jwtTokenResponse);
    }
    [HttpPost("register")]
    public async Task<IActionResult> Registration(AppUserRegistrationRequest registrationRequest)
    {
        await _serviceManager.AuthorizationService.RegisterAsync(registrationRequest);
        return Created();
    }

    [Authorize(AuthenticationSchemes = "IgnoreTokenExpirationScheme")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(AppRefreshTokenRequest refreshTokenRequest)
    {
        var refreshJwtTokenAsync = await _serviceManager.AuthorizationService.RefreshJwtTokenAsync(refreshTokenRequest);
        return Ok(refreshJwtTokenAsync);
    }
}