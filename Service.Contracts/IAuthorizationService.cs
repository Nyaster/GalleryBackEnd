using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAuthorizationService
{
    public Task<JwtTokenResponse> LoginAsync(AppLoginDto loginRequest);
    public Task RegisterAsync(CreateUserDto registrationRequest);
    public Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshhTokenResetDto refreshRequest, string accessToken);
}