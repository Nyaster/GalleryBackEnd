using Entities.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IAuthorizationService = GallerySiteBackend.Services.IAuthorizationService;

namespace GallerySiteBackend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthorizationController : ControllerBase
{
    private IAuthorizationService _authorizationService;

    public AuthorizationController(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login(AppLoginRequest loginRequest)
    {
        var jwtTokenResponse = await _authorizationService.LoginAsync(loginRequest);
        return Ok(jwtTokenResponse);
    }
    [HttpPost("register")]
    public async Task<IActionResult> Registration(AppUserRegistrationRequest registrationRequest)
    {
        await _authorizationService.RegisterAsync(registrationRequest);
        return Created();
    }

    [Authorize(AuthenticationSchemes = "IgnoreTokenExpirationScheme")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(AppRefreshTokenRequest refreshTokenRequest)
    {
        var refreshJwtTokenAsync = await _authorizationService.RefreshJwtTokenAsync(refreshTokenRequest);
        return Ok(refreshJwtTokenAsync);
    }
}