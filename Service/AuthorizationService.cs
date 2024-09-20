using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using Contracts;
using Entities.Exceptions;
using GallerySiteBackend.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Service.Contracts;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Service;

public class AuthorizationService : IAuthorizationService
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerManager _logger;
    private readonly IMapper _mapper;
    private readonly IRepositoryManager _repositoryManager;


    public AuthorizationService(IRepositoryManager repositoryManager, IConfiguration configuration,
        ILoggerManager logger, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _configuration = configuration;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<JwtTokenResponse> LoginAsync(AppLoginDto loginRequest)
    {
        var user = await _repositoryManager.AppUser.GetByLoginAsync(loginRequest.Login, false);
        if (user == null) throw new AppUserNotFoundException("User not found");

        var passwordHashFromDb = user.Password;
        var curHashedPassword = SecurityHelpers.GetHashedPasswordWithSalt(loginRequest.Password, user.Salt);

        if (passwordHashFromDb != curHashedPassword) throw new AppUserUnauthorizedException("Invalid credentials");


        var userclaim = new List<Claim>();
        userclaim.Add(new Claim(ClaimTypes.Name, user.Login));
        user.AppUserRolesList.ForEach(x => userclaim.Add(new Claim(ClaimTypes.Role, x.ToString())));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["SecretKey"]));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _configuration["Issuer"],
            _configuration["Issuer"],
            userclaim,
            expires: DateTime.Now.AddSeconds(30).ToUniversalTime(),
            signingCredentials: creds
        );

        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        _repositoryManager.AppUser.Update(user);
        await _repositoryManager.Save();

        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken
        };
    }

    public async Task RegisterAsync(CreateUserDto registrationRequest)
    {
        var userByLogin =
            await _repositoryManager.AppUser.GetByLoginAsync(registrationRequest.Login.ToLower(), false);
        if (userByLogin != null) throw new UserArleadyExistException($" {registrationRequest.Login} is already exist");

        var hashedPasswordAndSalt = SecurityHelpers.GetHashedPasswordAndSalt(registrationRequest.Password);
        var user = new AppUser
        {
            Login = registrationRequest.Login,
            Password = hashedPasswordAndSalt.Item1,
            Salt = hashedPasswordAndSalt.Item2,
            RefreshToken = SecurityHelpers.GenerateRefreshToken(),
            RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime(),
            AppUserRolesList = [AppUserRoles.User]
        };

        await _repositoryManager.AppUser.Create(user);
        await _repositoryManager.Save();
    }

    public async Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshhTokenResetDto refreshRequest,
        string accessToken)
    {
        var userLogin = SecurityHelpers.GetUserIdFromAccessToken(accessToken, _configuration["SecretKey"]);
        var user = await _repositoryManager.AppUser.GetByLoginAsync(userLogin, false);
        if (user == null) throw new SecurityTokenException("Invalid refresh token");

        if (user.RefreshToken != refreshRequest.RefreshToken) throw new SecurityTokenException("Invalid refresh token");

        if (user.IsRefreshTokenExpired) throw new SecurityTokenException("Refresh token expired");

        var userclaim = new List<Claim>();
        userclaim.Add(new Claim(ClaimTypes.Name, user.Login));
        user.AppUserRolesList.ForEach(x => userclaim.Add(new Claim(ClaimTypes.Role, x.ToString())));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["SecretKey"]));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwtToken = new JwtSecurityToken(
            _configuration["Issuer"],
            _configuration["Issuer"],
            userclaim,
            expires: DateTime.Now.AddMinutes(10).ToUniversalTime(),
            signingCredentials: creds
        );
        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        _repositoryManager.AppUser.Update(user);
        await _repositoryManager.Save();
        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(jwtToken),
            RefreshToken = user.RefreshToken
        };
    }
}