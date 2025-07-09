using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;
using Application.Features.Users.Helpers;
using Contracts;
using Entities.Exceptions;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Models;
using MediatR;
using Microsoft.Extensions.Options;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Application.Features.Users.RegisterNewUser;

public class Handler(IRepositoryManager repositoryManager, IOptions<JwtConfiguration> jwtConfig)
    : IRequestHandler<Command, JwtTokenResponse>
{
    IOptions<JwtConfiguration> JwtConfig { get; } = jwtConfig;

    public async Task<JwtTokenResponse> Handle(Command request, CancellationToken cancellationToken)
    {
        var registrationRequest = request.RegistrationRequest;
        var login = registrationRequest.Login.ToLower().Trim();
        if (!Regex.IsMatch(login, "^[a-zA-Z0-9]+$"))
        {
            throw new InvalidLoginException("Login can contain only alphanumeric characters (letters and digits).");
        }

        var existingUser = await repositoryManager.AppUser.GetByLoginAsync(login, false);
        if (existingUser != null)
        {
            throw new UserArleadyExistException($"{registrationRequest.Login} is already exist");
        }

        var (hashedPassword, salt) = SecurityHelpers.GetHashedPasswordAndSalt(registrationRequest.Password);
        var user = new AppUser()
        {
            Login = login,
            Password = hashedPassword,
            Salt = salt,
            RefreshToken = SecurityHelpers.GenerateRefreshToken(),
            RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime(),
            AppUserRolesList = [AppUserRoles.User]
        };
        await repositoryManager.AppUser.Create(user);
        await repositoryManager.Save();

        var token = AuthorizationHelpers.GenerateJwtToken(user, JwtConfig.Value);
        return new JwtTokenResponse()
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken,
        };
    }
}