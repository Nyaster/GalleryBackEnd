
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Models;
using Microsoft.IdentityModel.Tokens;
using Service.Helpers;

namespace Application.Features.Users.Helpers;

public static class AuthorizationHelpers
{
    public static JwtSecurityToken GenerateJwtToken(AppUser user, JwtConfiguration jwtConfiguration)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, user.Login) };
        user.AppUserRolesList.ForEach(x => claims.Add(new Claim(ClaimTypes.Role, x.ToString())));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfiguration.SecretKey));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            jwtConfiguration.ValidIssuer,
            jwtConfiguration.ValidAudience,
            claims,
            expires: DateTime.Now.AddMinutes(10).ToUniversalTime(),
            signingCredentials: credentials
        );

        user.RefreshToken = SecurityHelpers.GenerateRefreshToken();
        user.RefreshTokenExp = DateTime.Now.AddDays(1).ToUniversalTime();
        return token;
    }
}