using System.IdentityModel.Tokens.Jwt;
using Application.Features.Users.Helpers;
using Contracts;
using GallerySiteBackend.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Application.Features.Users.Commands;

public class RefreshTokenCommandHandler(IRepositoryManager repositoryManager, IOptions<JwtConfiguration> jwtConfig)
    : IRequestHandler<RefreshTokenCommand, JwtTokenResponse>
{
    public async Task<JwtTokenResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var accessToken = request.Token;
        var jwtConfiguration = jwtConfig.Value;
        var refreshRequest = request.RefreshRequest;
        var userLogin = SecurityHelpers.GetUserIdFromAccessToken(accessToken, jwtConfiguration.SecretKey);
        var user = await repositoryManager.AppUser.GetByLoginAsync(userLogin, false);
        if (user == null) throw new SecurityTokenException("Invalid refresh token");

        if (user.RefreshToken != refreshRequest.RefreshToken) throw new SecurityTokenException("Invalid refresh token");

        if (user.IsRefreshTokenExpired) throw new SecurityTokenException("Refresh token expired");

        var jwtToken = AuthorizationHelpers.GenerateJwtToken(user, jwtConfiguration);
        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        repositoryManager.AppUser.Update(user);
        await repositoryManager.Save();
        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
            RefreshToken = user.RefreshToken
        };
    }
}