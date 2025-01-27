using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
        var user = await _repositoryManager.AppUser.GetByLoginAsync(loginRequest.Login.ToLower().Trim(), false);
        if (user == null) throw new AppUserNotFoundException("User not found");

        var passwordHashFromDb = user.Password;
        var curHashedPassword = SecurityHelpers.GetHashedPasswordWithSalt(loginRequest.Password, user.Salt);

        if (passwordHashFromDb != curHashedPassword) throw new AppUserUnauthorizedException("Invalid credentials");


        var token = GenerateJwtSecurityToken(user);
        _repositoryManager.AppUser.Update(user);
        await _repositoryManager.Save();

        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken
        };
    }

    private JwtSecurityToken GenerateJwtSecurityToken(AppUser user)
    {
        var userclaim = new List<Claim>();
        userclaim.Add(new Claim(ClaimTypes.Name, user.Login));
        user.AppUserRolesList.ForEach(x => userclaim.Add(new Claim(ClaimTypes.Role, x.ToString())));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["SecretKey"]));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _configuration["Issuer"],
            _configuration["Issuer"],
            userclaim,
            expires: DateTime.Now.AddMinutes(5).ToUniversalTime(),
            signingCredentials: creds
        );

        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        return token;
    }

    public async Task<JwtTokenResponse> RegisterAsync(CreateUserDto registrationRequest)
    {
        var login = registrationRequest.Login.ToLower().Trim();
        if (!Regex.IsMatch(login, "^[a-zA-Z0-9]+$"))
        {
            throw new InvalidLoginException("Login can contain only alphanumeric characters (letters and digits).");
        }
        var userByLogin =
            await _repositoryManager.AppUser.GetByLoginAsync(login, false);
        if (userByLogin != null) throw new UserArleadyExistException($" {registrationRequest.Login} is already exist");

        var hashedPasswordAndSalt = SecurityHelpers.GetHashedPasswordAndSalt(registrationRequest.Password);
        var user = new AppUser
        {
            Login = login,
            Password = hashedPasswordAndSalt.Item1,
            Salt = hashedPasswordAndSalt.Item2,
            RefreshToken = SecurityHelpers.GenerateRefreshToken(),
            RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime(),
            AppUserRolesList = [AppUserRoles.User]
        };

        await _repositoryManager.AppUser.Create(user);
        await _repositoryManager.Save();
        var token = GenerateJwtSecurityToken(user);
        return new JwtTokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken
        };
    }

    public async Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshhTokenResetDto refreshRequest,
        string accessToken)
    {
        var userLogin = SecurityHelpers.GetUserIdFromAccessToken(accessToken, _configuration["SecretKey"]);
        var user = await _repositoryManager.AppUser.GetByLoginAsync(userLogin, false);
        if (user == null) throw new SecurityTokenException("Invalid refresh token");

        if (user.RefreshToken != refreshRequest.RefreshToken) throw new SecurityTokenException("Invalid refresh token");

        if (user.IsRefreshTokenExpired) throw new SecurityTokenException("Refresh token expired");

        var jwtToken = GenerateJwtSecurityToken(user);
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