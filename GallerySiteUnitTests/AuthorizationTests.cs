using GallerySiteBackend.Exceptions;
using GallerySiteBackend.Helpers;
using GallerySiteBackend.Models;
using GallerySiteBackend.Models.Requests;
using GallerySiteBackend.Repositories;
using GallerySiteBackend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace GallerySiteUnitTests;

public class AuthorizationTests
{
    private readonly Mock<IAppUserRepository> _userRepositoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly AuthorizationService _authorizationService;

    public AuthorizationTests()
    {
        _userRepositoryMock = new Mock<IAppUserRepository>();
        _configurationMock = new Mock<IConfiguration>();
        _configurationMock.SetupGet(x => x["SecretKey"])
            .Returns("TestSecretKeyTestSecretKeyTestSecretKeyTestSecretKeyTestSecretKeyTestSecretKey");
        _configurationMock.SetupGet(x => x["Issuer"]).Returns("localhost:5000");
        _authorizationService = new AuthorizationService(_userRepositoryMock.Object, _configurationMock.Object);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnTokenResponse_WhenCreditionalAreValid()
    {
        //Arrange
        var loginRequest = new AppLoginRequest() { Login = "TestUser", Password = "Password" };
        var appUser = new AppUser()
        {
            Login = "TestUser",
            Password = SecurityHelpers.GetHashedPasswordWithSalt("Password", "salt"),
            Salt = "salt",
            AppUserRolesList = [AppUserRoles.User]
        };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(loginRequest.Login)).ReturnsAsync(appUser);
        //Act
        var result = await _authorizationService.LoginAsync(loginRequest);
        //Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.NotEmpty(result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnAppUserNotFoundException_WhenUserNotFound()
    {
        //Arrange
        var loginRequest = new AppLoginRequest() { Login = "TestUser", Password = "Password" };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(loginRequest.Login)).ReturnsAsync((AppUser?)null);
        //Act & Assert
        await Assert.ThrowsAsync<AppUserNotFoundException>(() => _authorizationService.LoginAsync(loginRequest));
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowAppUserUnauthorizedException_WhenPasswordIsInvalid()
    {
        //Arrange
        var loginRequest = new AppLoginRequest() { Login = "TestUser", Password = "Password" };
        var appUser = new AppUser()
        {
            Login = "TestUser",
            Password = SecurityHelpers.GetHashedPasswordWithSalt("asdfasqw", "salt"),
            Salt = "salt",
            AppUserRolesList = [AppUserRoles.User]
        };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(loginRequest.Login)).ReturnsAsync(appUser);
        //Act & Assert
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => _authorizationService.LoginAsync(loginRequest));
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowUserAlreadyExistException_WhenUserAlreadyExist()
    {
        //Arrange
        var registrationRequest = new AppUserRegistrationRequest() { Login = "TestUser", Password = "TestPassword" };
        var existingUser = new AppUser() { Login = "TestUser", Password = "test" };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(registrationRequest.Login.ToLower()))
            .ReturnsAsync(existingUser);
        //Act & Assert
        await Assert.ThrowsAsync<UserArleadyExistException>(() =>
            _authorizationService.RegisterAsync(registrationRequest));
    }

    [Fact]
    public async Task RegisterAsync_ShouldRegisterUser_WhenUserDoesNotExist()
    {
        //Arrange
        var registrationRequest = new AppUserRegistrationRequest() { Login = "TestUser", Password = "TestPassword" };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(registrationRequest.Login.ToLower()))
            .ReturnsAsync((AppUser?)null);
        //Act
        await _authorizationService.RegisterAsync(registrationRequest);
        //Assert
        _userRepositoryMock.Verify(x => x.Add(It.IsAny<AppUser>()), Times.Once);
    }

    [Fact]
    public async Task RefreshJwtTokenAsync_ShouldReturnTokenResponse_WhenRefreshTokenIsValid()
    {
        // Arrange
        var refreshRequest = new AppRefreshTokenRequest { RefreshToken = "validrefreshtoken" };
        var appUser = new AppUser
        {
            Login = "testuser",
            RefreshToken = "validrefreshtoken",
            RefreshTokenExp = DateTime.Now.AddDays(1),
            AppUserRolesList = [AppUserRoles.User],
            Password = "test"
        };

        _userRepositoryMock.Setup(r => r.GetByRefreshTokenAsync(refreshRequest.RefreshToken)).ReturnsAsync(appUser);

        // Act
        var result = await _authorizationService.RefreshJwtTokenAsync(refreshRequest);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Token);
        Assert.NotEmpty(result.RefreshToken);
    }

    [Fact]
    public async Task RefreshJwtTokenAsync_ShouldThrowSecurityTokenException_WhenRefreshTokenIsInvalid()
    {
        // Arrange
        var refreshRequest = new AppRefreshTokenRequest { RefreshToken = "invalidrefreshtoken" };
        _userRepositoryMock.Setup(r => r.GetByRefreshTokenAsync(refreshRequest.RefreshToken))
            .ReturnsAsync((AppUser)null);

        // Act & Assert
        await Assert.ThrowsAsync<SecurityTokenException>(() =>
            _authorizationService.RefreshJwtTokenAsync(refreshRequest));
    }

    [Fact]
    public async Task RefreshJwtTokenAsync_ShouldThrowSecurityTokenException_WhenRefreshTokenIsExpired()
    {
        // Arrange
        var refreshRequest = new AppRefreshTokenRequest { RefreshToken = "expiredrefreshtoken" };
        var appUser = new AppUser
        {
            Login = "testuser",
            RefreshToken = "expiredrefreshtoken",
            RefreshTokenExp = DateTime.Now.AddDays(-1),
            AppUserRolesList = [AppUserRoles.User],
            Password = "test"
        };

        _userRepositoryMock.Setup(r => r.GetByRefreshTokenAsync(refreshRequest.RefreshToken)).ReturnsAsync(appUser);

        // Act & Assert
        await Assert.ThrowsAsync<SecurityTokenException>(() =>
            _authorizationService.RefreshJwtTokenAsync(refreshRequest));
    }
}