using Application.Features.Users.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthorizationController(IServiceManager serviceManager, IMediator mediator) : ControllerBase
{


    [HttpPost("login")]
    public async Task<IActionResult> Login(AppLoginDto loginRequest)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var request = new AuthorizeUserCommand(loginRequest);
        var result = await mediator.Send(request);
        return Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Registration(CreateUserDto registrationRequest)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var command = new RegisterUserCommand(registrationRequest);
        var response = await mediator.Send(command);
        return Ok(response);
    }

    [Authorize(AuthenticationSchemes = "IgnoreTokenExpirationScheme")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(AppRefreshhTokenResetDto refreshTokenRequest)
    {
        var token = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var command = new RefreshTokenCommand(refreshTokenRequest, token);
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [Authorize(Roles = "User,Admin")]
    [HttpGet("test")]
    public async Task<IActionResult> CheckUpdates()
    {
        var claims = HttpContext.User.Claims.Select(x => new { x.Type, x.Value }).ToList();
        return Ok(claims);
    }
}