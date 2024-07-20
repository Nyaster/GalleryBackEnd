using GallerySiteBackend.Models.DTOs;
using GallerySiteBackend.Models.Requests;
using Microsoft.AspNetCore.Identity.Data;

namespace GallerySiteBackend.Services;

public interface IAuthorizationService
{
    public Task<JwtTokenResponse> LoginAsync(AppLoginRequest loginRequest);
    public Task RegisterAsync(AppUserRegistrationRequest registrationRequest);
    public Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshTokenRequest refreshRequest);
    
}