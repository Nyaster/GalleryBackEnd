using Entities.Models.Requests;
using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAuthorizationService
{
    public Task<JwtTokenResponse> LoginAsync(AppLoginRequest loginRequest);
    public Task RegisterAsync(AppUserRegistrationRequest registrationRequest);
    public Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshTokenRequest refreshRequest);
    
}