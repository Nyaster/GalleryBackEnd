using Application.Features.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using GallerySiteBackend.Configuration;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Route("api/auth")]
public sealed class AuthorizationController(IMediator mediator, IOptions<JwtConfiguration> jwt) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    [ProducesResponseType<JwtTokenResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<JwtTokenResponse>> Register(CreateUserDto request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new Application.Features.Users.RegisterNewUser.Command(request),
            cancellationToken);
        SetRefreshCookie(result);
        return StatusCode(StatusCodes.Status201Created, result.Response);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [ProducesResponseType<JwtTokenResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<JwtTokenResponse>> Login(AppLoginDto request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new Application.Features.Users.AuthorizeUser.Command(request),
            cancellationToken);
        SetRefreshCookie(result);
        return Ok(result.Response);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    [ProducesResponseType<JwtTokenResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<JwtTokenResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(jwt.Value.RefreshCookieName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized();
        var result = await mediator.Send(new Application.Features.Users.RefreshUserToken.Command(refreshToken),
            cancellationToken);
        SetRefreshCookie(result);
        return Ok(result.Response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(jwt.Value.RefreshCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
            await mediator.Send(new Application.Features.Users.LogoutUser.Command(refreshToken), cancellationToken);
        Response.Cookies.Delete(jwt.Value.RefreshCookieName, RefreshCookieOptions());
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AppUserDto>> Me(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCurrentUserQuery(), cancellationToken));

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("authenticator/setup")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AuthenticatorSetupResponse>> SetupAuthenticator(AuthenticatorSetupDto request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SetupAuthenticatorCommand(request), cancellationToken));

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("authenticator/confirm")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AuthenticatorBackupCodesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticatorBackupCodesResponse>> ConfirmAuthenticator(
        AuthenticatorConfirmDto request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ConfirmAuthenticatorCommand(request), cancellationToken));

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("authenticator/backup-codes")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AuthenticatorBackupCodesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticatorBackupCodesResponse>> RegenerateBackupCodes(
        RegenerateBackupCodesDto request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RegenerateBackupCodesCommand(request), cancellationToken));

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("authenticator/remove")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAuthenticator(AuthenticatorRemoveDto request,
        CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveAuthenticatorCommand(request), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("recover")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Recover(RecoverAccountDto request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RecoverAccountCommand(request), cancellationToken);
        Response.Cookies.Delete(jwt.Value.RefreshCookieName, RefreshCookieOptions());
        return NoContent();
    }

    private void SetRefreshCookie(AuthCommandResult result)
        => Response.Cookies.Append(jwt.Value.RefreshCookieName, result.RefreshToken, RefreshCookieOptions());

    private CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = jwt.Value.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        IsEssential = true,
        Expires = DateTimeOffset.UtcNow.AddDays(jwt.Value.RefreshTokenDays)
    };
}