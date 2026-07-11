using System.Security.Cryptography;
using System.Text;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class AuthenticationServiceTests
{
    private static readonly JwtConfiguration Jwt = new()
    {
        ValidIssuer = "https://gallery.test",
        ValidAudience = "https://gallery.test",
        SecretKey = "test-key-must-be-at-least-thirty-two-bytes-long",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 7
    };

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesHashedUserAndRefreshSession()
    {
        var users = new Mock<IAppUserRepository>();
        var repositories = RepositoryMock(users);
        AppUser? createdUser = null;
        RefreshSession? createdSession = null;
        users.Setup(repo => repo.GetByNormalizedLoginAsync("GALLERYUSER", false, It.IsAny<CancellationToken>())).ReturnsAsync((AppUser?)null);
        users.Setup(repo => repo.AddAsync(It.IsAny<AppUser>(), It.IsAny<CancellationToken>()))
            .Callback<AppUser, CancellationToken>((user, _) => { user.Id = 42; createdUser = user; }).Returns(Task.CompletedTask);
        users.Setup(repo => repo.AddRefreshSessionAsync(It.IsAny<RefreshSession>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshSession, CancellationToken>((session, _) => createdSession = session).Returns(Task.CompletedTask);

        var service = CreateService(repositories.Object);
        var result = await service.RegisterAsync(new CreateUserDto("GalleryUser", "a-long-and-valid-password"));

        Assert.NotNull(createdUser);
        Assert.NotEqual("a-long-and-valid-password", createdUser.PasswordHash);
        Assert.NotNull(createdSession);
        Assert.Equal(42, createdSession.UserId);
        Assert.NotEmpty(result.Response.AccessToken);
        Assert.StartsWith(createdSession.Id.ToString(), result.RefreshToken, StringComparison.Ordinal);
        repositories.Verify(repo => repo.SaveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LoginAsync_UnknownLogin_ReturnsSameUnauthorizedErrorAsBadPassword()
    {
        var users = new Mock<IAppUserRepository>();
        users.Setup(repo => repo.GetByNormalizedLoginAsync("MISSINGUSER", true, It.IsAny<CancellationToken>())).ReturnsAsync((AppUser?)null);

        var exception = await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            CreateService(RepositoryMock(users).Object).LoginAsync(new AppLoginDto("MissingUser", "a-long-and-valid-password")));

        Assert.Equal("Invalid login or password.", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_ActiveSession_RotatesAndRevokesPriorSession()
    {
        var user = new AppUser
        {
            Id = 7,
            Login = "galleryuser",
            NormalizedLogin = "GALLERYUSER",
            PasswordHash = "unused",
            Roles = [AppUserRole.User],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var refreshToken = $"{Guid.NewGuid()}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))}";
        var existing = new RefreshSession
        {
            Id = Guid.Parse(refreshToken.Split('.', 2)[0]),
            UserId = user.Id,
            User = user,
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)),
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        var users = new Mock<IAppUserRepository>();
        users.Setup(repo => repo.GetRefreshSessionAsync(It.IsAny<byte[]>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        RefreshSession? replacement = null;
        users.Setup(repo => repo.AddRefreshSessionAsync(It.IsAny<RefreshSession>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshSession, CancellationToken>((session, _) => replacement = session).Returns(Task.CompletedTask);

        var result = await CreateService(RepositoryMock(users).Object).RefreshAsync(refreshToken);

        Assert.NotNull(replacement);
        Assert.NotNull(existing.RevokedAtUtc);
        Assert.Equal("rotated", existing.RevokeReason);
        Assert.Equal(replacement.Id, existing.ReplacedBySessionId);
        Assert.StartsWith(replacement.Id.ToString(), result.RefreshToken, StringComparison.Ordinal);
    }

    private static Mock<IRepositoryManager> RepositoryMock(Mock<IAppUserRepository> users)
    {
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repo => repo.AppUser).Returns(users.Object);
        repositories.Setup(repo => repo.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repositories;
    }

    private static AuthenticationService CreateService(IRepositoryManager repositories)
        => new(repositories, new PasswordHasher<AppUser>(), Options.Create(Jwt), TimeProvider.System);
}
