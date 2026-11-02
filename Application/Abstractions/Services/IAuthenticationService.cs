using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAuthenticationService
{
    Task<AuthenticationResult> RegisterAsync(CreateUserDto request, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> LoginAsync(AppLoginDto request, CancellationToken cancellationToken = default);
    Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public sealed record AuthenticationResult(JwtTokenResponse Response, string RefreshToken);
