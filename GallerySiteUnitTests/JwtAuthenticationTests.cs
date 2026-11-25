using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Entities.Models;
using Service.Contracts;
using Shared.DataTransferObjects;
using GallerySiteBackend.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace GallerySiteUnitTests;

public sealed class JwtAuthenticationTests
{
    private const string Issuer = "https://gallery.test";
    private const string Audience = "https://gallery.test";
    private const string SigningKey = "jwt-validation-tests-only-key-more-than-32-bytes";

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("42")]
    public async Task ValidAccessToken_UsesCurrentAccountPermissionsRegardlessOfLegacyClaims(string? legacyVersion)
    {
        var access = new Mock<IRoleAccessService>(MockBehavior.Strict);
        access.Setup(service => service.GetAccessAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountAccessDto(["Tag Reviewer"], [AccessPermission.ImageTagsReview]));
        using var services = CreateServices(access.Object);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "123"), new(ClaimTypes.Role, "Admin"),
            new(AccessPermission.ClaimType, AccessPermission.RolesManage)
        };
        if (legacyVersion is not null) claims.Add(new Claim("auth_version", legacyVersion));

        var result = await AuthenticateAsync(services, CreateToken(claims));

        Assert.True(result.Succeeded, result.Failure?.ToString());
        Assert.Equal("123", result.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.False(result.Principal.IsInRole("Admin"));
        Assert.True(result.Principal.IsInRole("Tag Reviewer"));
        Assert.False(result.Principal.HasClaim(AccessPermission.ClaimType, AccessPermission.RolesManage));
        Assert.True(result.Principal.HasClaim(AccessPermission.ClaimType, AccessPermission.ImageTagsReview));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("unsigned")]
    [InlineData("missing-user")]
    [InlineData("invalid-user")]
    public async Task InvalidAccessToken_IsRejectedWithoutRepositoryAccess(string invalidPart)
    {
        var repositories = new Mock<IRoleAccessService>(MockBehavior.Strict);
        using var services = CreateServices(repositories.Object);
        var claims = new List<Claim>();
        if (invalidPart != "missing-user")
            claims.Add(new Claim(ClaimTypes.NameIdentifier, invalidPart == "invalid-user" ? "not-an-id" : "123"));
        var token = CreateToken(claims, invalidPart);

        var result = await AuthenticateAsync(services, token);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        repositories.VerifyNoOtherCalls();
    }

    private static ServiceProvider CreateServices(IRoleAccessService repositories)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtConfig:ValidIssuer"] = Issuer,
            ["JwtConfig:ValidAudience"] = Audience,
            ["JwtConfig:SecretKey"] = SigningKey
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(repositories);
        services.ConfigureJwtToken(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(IServiceProvider services, string token)
    {
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + token;
        return await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
    }

    private static string CreateToken(IEnumerable<Claim> claims, string? invalidPart = null)
    {
        var now = DateTime.UtcNow;
        var key = invalidPart == "signature" ? SigningKey + "-wrong" : SigningKey;
        var token = new JwtSecurityToken(
            invalidPart == "issuer" ? "https://wrong-issuer.test" : Issuer,
            invalidPart == "audience" ? "https://wrong-audience.test" : Audience,
            claims,
            notBefore: invalidPart == "future" ? now.AddMinutes(5) : now.AddMinutes(-30),
            expires: invalidPart == "expired" ? now.AddMinutes(-1) : now.AddMinutes(15),
            signingCredentials: invalidPart == "unsigned" ? null : new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
