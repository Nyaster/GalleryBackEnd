using System.Collections;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GallerySiteBackend.Exceptions;
using GallerySiteBackend.Helpers;
using GallerySiteBackend.Models;
using GallerySiteBackend.Models.DTOs;
using GallerySiteBackend.Models.Requests;
using GallerySiteBackend.Repositories;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.IdentityModel.Tokens;

namespace GallerySiteBackend.Services;

public class AuthorizationService : IAuthorizationService
{
    private IAppUserRepository _userRepository;
    private IConfiguration _configuration;

    public AuthorizationService(IAppUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<JwtTokenResponse> LoginAsync(AppLoginRequest loginRequest)
    {
        AppUser? user = await _userRepository.GetByLoginAsync(loginRequest.Login);
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
        await _userRepository.Update(user);

        return new JwtTokenResponse()
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = user.RefreshToken
        };
        ;
    }

    public async Task RegisterAsync(AppUserRegistrationRequest registrationRequest)
    {
        var userByLogin = await _userRepository.GetByLoginAsync(registrationRequest.Login.ToLower());
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

        await _userRepository.Add(user);
    }

    public async Task<JwtTokenResponse> RefreshJwtTokenAsync(AppRefreshTokenRequest refreshRequest)
    {
        AppUser? user = await _userRepository.GetByRefreshTokenAsync(refreshRequest.RefreshToken);
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