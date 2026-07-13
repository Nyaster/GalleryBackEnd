using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public sealed class AuthenticationService(
    IRepositoryManager repositories,
    IPasswordHasher<AppUser> passwordHasher,
    IOptions<JwtConfiguration> options,
    TimeProvider clock,
    ILoggerFactory loggerFactory) : IAuthenticationService
{
    private static readonly Regex LoginPattern = new("^[a-zA-Z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ILogger _securityLogger = loggerFactory.CreateLogger("GallerySiteBackend.Security");

    public async Task<AuthenticationResult> RegisterAsync(CreateUserDto request, CancellationToken cancellationToken = default)
    {
        var login = NormalizeLogin(request.Login);
        if (!LoginPattern.IsMatch(login))
            throw new InvalidLoginException("Login may contain only letters and digits.");

        if (await repositories.AppUser.GetByNormalizedLoginAsync(login, false, cancellationToken) is not null)
            throw new UserArleadyExistException("This login is already registered.");

        var user = new AppUser
        {
            Login = request.Login.Trim(),
            NormalizedLogin = login,
            PasswordHash = string.Empty,
            CreatedAtUtc = clock.GetUtcNow()
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        await repositories.AppUser.AddAsync(user, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task<AuthenticationResult> LoginAsync(AppLoginDto request, CancellationToken cancellationToken = default)
    {
        var normalizedLogin = NormalizeLogin(request.Login);
        var user = await repositories.AppUser.GetByNormalizedLoginAsync(normalizedLogin, true, cancellationToken);
        if (user is null)
            throw new AppUserUnauthorizedException("Invalid login or password.");

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            throw new AppUserUnauthorizedException("Invalid login or password.");

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await repositories.SaveAsync(cancellationToken);
        }
        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);
        var now = clock.GetUtcNow();
        var replacement = CreateRefreshSession(0, now);
        var rotation = await repositories.AppUser.RotateRefreshSessionAsync(tokenHash, replacement.Session, now, cancellationToken);
        if (rotation.User is null)
        {
            _securityLogger.LogWarning("RefreshTokenRejected {TimestampUtc} {UserId} {FamilyRevoked}",
                now, rotation.UserId, rotation.FamilyRevoked);
            throw new AppUserUnauthorizedException("Refresh session is invalid or expired.");
        }
        return new AuthenticationResult(CreateJwtResponse(rotation.User, now), replacement.RawToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var session = await repositories.AppUser.GetRefreshSessionAsync(HashToken(refreshToken), true, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null)
            return;
        session.RevokedAtUtc = clock.GetUtcNow();
        session.RevokeReason = "logout";
        await repositories.SaveAsync(cancellationToken);
    }

    private async Task<AuthenticationResult> CreateSessionAsync(AppUser user, CancellationToken cancellationToken, bool save = true)
    {
        var now = clock.GetUtcNow();
        var created = CreateRefreshSession(user.Id, now);
        var session = created.Session;
        await repositories.AppUser.AddRefreshSessionAsync(session, cancellationToken);
        if (save)
            await repositories.SaveAsync(cancellationToken);
        return new AuthenticationResult(CreateJwtResponse(user, now), created.RawToken);
    }

    private (RefreshSession Session, string RawToken) CreateRefreshSession(int userId, DateTimeOffset now)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var sessionId = Guid.NewGuid();
        var refreshToken = $"{sessionId}.{rawToken}";
        return (new RefreshSession { Id = sessionId, UserId = userId, FamilyId = Guid.NewGuid(), TokenHash = HashToken(refreshToken),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(options.Value.RefreshTokenDays) }, refreshToken);
    }

    private JwtTokenResponse CreateJwtResponse(AppUser user, DateTimeOffset now)
    {
        var config = options.Value;
        var expires = now.AddMinutes(config.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SecretKey));
        var token = new JwtSecurityToken(config.ValidIssuer, config.ValidAudience, claims, now.UtcDateTime,
            expires.UtcDateTime, new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires,
            new AppUserDto(user.Id, user.Login, user.Roles.Select(role => role.ToString()).ToArray()));
    }

    private static string NormalizeLogin(string login) => login.Trim().ToUpperInvariant();
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
