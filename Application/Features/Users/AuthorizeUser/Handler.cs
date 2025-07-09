using System.IdentityModel.Tokens.Jwt;
using Application.Features.Users.Helpers;
using Contracts;
using Entities.Exceptions;
using GallerySiteBackend.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Application.Features.Users.AuthorizeUser;

public class Handler(IRepositoryManager repositoryManager, IOptions<JwtConfiguration> jwtConfig)
    : IRequestHandler<Command, JwtTokenResponse>
{
    public async Task<JwtTokenResponse> Handle(Command request, CancellationToken cancellationToken)
    {
        var loginRequest = request.AppLoginDto;
        var user = await repositoryManager.AppUser.GetByLoginAsync(loginRequest.Login.ToLower().Trim(), false);
        if (user == null) throw new AppUserNotFoundException("User not found");

        var passwordHashFromDb = user.Password;
        var curHashedPassword = SecurityHelpers.GetHashedPasswordWithSalt(loginRequest.Password, user.Salt);

        if (passwordHashFromDb != curHashedPassword) throw new AppUserUnauthorizedException("Invalid credentials");


        var token = AuthorizationHelpers.GenerateJwtToken(user, jwtConfig.Value);
        repositoryManager.AppUser.Update(user);
        await repositoryManager.Save();

        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken!
        };
    }
}