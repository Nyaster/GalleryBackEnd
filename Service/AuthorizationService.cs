using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Entities.Models.Requests;
using GallerySiteBackend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Service.Contracts;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Service;

public class AuthorizationService : IAuthorizationService
{
    private IRepositoryManager _repositoryManager;
    private IConfiguration _configuration;
    private readonly ILoggerManager _logger;
    private readonly IMapper _mapper;


    public AuthorizationService(IRepositoryManager repositoryManager, IConfiguration configuration, ILoggerManager logger, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _configuration = configuration;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<JwtTokenResponse> LoginAsync(AppLoginRequest loginRequest)
    {
        AppUser? user = await _repositoryManager.AppUser.GetByLoginAsync(loginRequest.Login);
        if (user == null)
        {
            throw new AppUserNotFoundException("User not found");
        }

        string passwordHashFromDb = user.Password;
        string curHashedPassword = SecurityHelpers.GetHashedPasswordWithSalt(loginRequest.Password, user.Salt);

        if (passwordHashFromDb != curHashedPassword)
        {
            throw new AppUserUnauthorizedException("Invalid credentials");
        }


        List<Claim> userclaim = new List<Claim>();
        userclaim.Add(new Claim(ClaimTypes.Name, user.Login));
        user.AppUserRolesList.ForEach(x => userclaim.Add(new Claim(ClaimTypes.Role, x.ToString())));
        SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["SecretKey"]));

        SigningCredentials creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _configuration["Issuer"],
            audience: _configuration["Issuer"],
            claims: userclaim,
            expires: DateTime.Now.AddMinutes(15).ToUniversalTime(),
            signingCredentials: creds
        );

        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        _repositoryManager.AppUser.Update(user);

        return new JwtTokenResponse()
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken
        };
        ;
    }

    public async Task RegisterAsync(AppUserRegistrationRequest registrationRequest)
    {
        var userByLogin = await _repositoryManager.AppUser.GetByLoginAsync(registrationRequest.Login.ToLower());
        if (userByLogin != null)
        {
            throw new UserArleadyExistException($" {registrationRequest.Login} is already exist");
        }

        var hashedPasswordAndSalt = SecurityHelpers.GetHashedPasswordAndSalt(registrationRequest.Password);
        var user = new AppUser()
        {
            Login = registrationRequest.Login,
            Password = hashedPasswordAndSalt.Item1,
            Salt = hashedPasswordAndSalt.Item2,
            RefreshToken = SecurityHelpers.GenerateRefreshToken(),
            RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime(),
            AppUserRolesList = [AppUserRoles.User],
        };

        await _repositoryManager.AppUser.Create(user);
        await _repositoryManager.Save();
    }

    public async Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshTokenRequest refreshRequest)
    {
        AppUser? user = await _repositoryManager.AppUser.GetByRefreshTokenAsync(refreshRequest.RefreshToken);
        if (user == null)
        {
            throw new SecurityTokenException("Invalid refresh token");
        }

        if (user.IsRefreshTokenExpired)
        {
            throw new SecurityTokenException("Refresh token expired");
        }

        List<Claim> userclaim = new List<Claim>();
        userclaim.Add(new Claim(ClaimTypes.Name, user.Login));
        user.AppUserRolesList.ForEach(x => userclaim.Add(new Claim(ClaimTypes.Role, x.ToString())));

        SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["SecretKey"]));

        SigningCredentials creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken jwtToken = new JwtSecurityToken(
            issuer: _configuration["Issuer"],
            audience: _configuration["Issuer"],
            claims: userclaim,
            expires: DateTime.Now.AddMinutes(10).ToUniversalTime(),
            signingCredentials: creds
        );

        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();

        return new JwtTokenResponse()
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
            RefreshToken = user.RefreshToken
        };
    }
}