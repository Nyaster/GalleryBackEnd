using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.Extensions.Options;
using Moq;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class UploadPermissionHandlerTests
{
    [Fact]
    public async Task UploadImage_UngrantedModerator_ThrowsForbiddenBeforeFileProcessing()
    {
        var users = UserRepository(User(7, canUploadImages: false));
        var storage = new Mock<IImageStorage>();
        var handler = UploadHandler(Repositories(users).Object, new TestUser(7, AppUserRole.Moderator), storage);

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(UploadCommand(), CancellationToken.None));

        storage.Verify(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_GrantedModerator_ProceedsPastPermissionGate()
    {
        var users = UserRepository(User(7, canUploadImages: true));
        var handler = UploadHandler(Repositories(users).Object, new TestUser(7, AppUserRole.Moderator));

        await Assert.ThrowsAsync<ImageUploadValidationError>(() => handler.Handle(UploadCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task UploadImage_RevokedUser_ThrowsForbidden()
    {
        var users = UserRepository(User(7, canUploadImages: false));
        var handler = UploadHandler(Repositories(users).Object, new TestUser(7));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(UploadCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task UploadImage_AdminBypassesStoredPermission()
    {
        var users = new Mock<IAppUserRepository>();
        var handler = UploadHandler(Repositories(users).Object, new TestUser(7, AppUserRole.Admin));

        await Assert.ThrowsAsync<ImageUploadValidationError>(() => handler.Handle(UploadCommand(), CancellationToken.None));

        users.Verify(repository => repository.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AiUsageClassification.Unknown)]
    [InlineData((AiUsageClassification)999)]
    public async Task UploadImage_InvalidAiUsage_RejectsBeforeFileProcessing(AiUsageClassification aiUsage)
    {
        var storage = new Mock<IImageStorage>();
        var handler = UploadHandler(Repositories(new Mock<IAppUserRepository>()).Object, new TestUser(7, AppUserRole.Admin), storage);

        await Assert.ThrowsAsync<ImageUploadValidationError>(() => handler.Handle(
            new Application.Features.Images.UploadImage.Command(new AppImageCreationDto { AiUsage = aiUsage }),
            CancellationToken.None));

        storage.Verify(service => service.SaveTemporaryAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_MissingCurrentUserRecord_ThrowsForbidden()
    {
        var users = new Mock<IAppUserRepository>();
        users.Setup(repository => repository.GetByIdAsync(7, false, It.IsAny<CancellationToken>())).ReturnsAsync((AppUser?)null);
        var handler = UploadHandler(Repositories(users).Object, new TestUser(7));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(UploadCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUploadPermission_GrantAndRevoke_PersistsBothValues()
    {
        var user = User(12, canUploadImages: false);
        var users = UserRepository(user);
        var repositories = Repositories(users);
        var handler = new Application.Features.Administration.UpdateUserUploadPermission.Handler(repositories.Object, new TestUser(1, AppUserRole.Admin));

        var granted = await handler.Handle(new Application.Features.Administration.UpdateUserUploadPermission.Command(12, true), CancellationToken.None);
        var revoked = await handler.Handle(new Application.Features.Administration.UpdateUserUploadPermission.Command(12, false), CancellationToken.None);

        Assert.Equal(12, granted.Id);
        Assert.Equal("target", granted.Login);
        Assert.True(granted.CanUploadImages);
        Assert.False(revoked.CanUploadImages);
        Assert.False(user.CanUploadImages);
        repositories.Verify(repository => repository.SaveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetUploadPermission_AdminUser_ReturnsEffectiveAccessWhenStoredFlagIsFalse()
    {
        var users = UserRepository(User(12, canUploadImages: false, AppUserRole.Admin));
        var handler = new Application.Features.Administration.GetUserUploadPermission.Handler(
            Repositories(users).Object, new TestUser(1, AppUserRole.Admin));

        var result = await handler.Handle(new Application.Features.Administration.GetUserUploadPermission.Command(12), CancellationToken.None);

        Assert.True(result.CanUploadImages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadPermission_UnknownUser_ReturnsNotFound(bool isUpdate)
    {
        var users = new Mock<IAppUserRepository>();
        users.Setup(repository => repository.GetByIdAsync(404, isUpdate, It.IsAny<CancellationToken>())).ReturnsAsync((AppUser?)null);
        var repositories = Repositories(users);

        if (isUpdate)
        {
            var handler = new Application.Features.Administration.UpdateUserUploadPermission.Handler(repositories.Object, new TestUser(1, AppUserRole.Admin));
            await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(
                new Application.Features.Administration.UpdateUserUploadPermission.Command(404, true), CancellationToken.None));
        }
        else
        {
            var handler = new Application.Features.Administration.GetUserUploadPermission.Handler(repositories.Object, new TestUser(1, AppUserRole.Admin));
            await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(
                new Application.Features.Administration.GetUserUploadPermission.Command(404), CancellationToken.None));
        }
    }

    [Fact]
    public async Task UploadPermission_Moderator_IsForbidden()
    {
        var users = new Mock<IAppUserRepository>();
        var handler = new Application.Features.Administration.GetUserUploadPermission.Handler(
            Repositories(users).Object, new TestUser(1, AppUserRole.Moderator));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(
            new Application.Features.Administration.GetUserUploadPermission.Command(12), CancellationToken.None));

        users.Verify(repository => repository.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Application.Features.Images.UploadImage.Handler UploadHandler(IRepositoryManager repositories, IUserContext currentUser,
        Mock<IImageStorage>? storage = null)
        => new(repositories, (storage ?? new Mock<IImageStorage>()).Object, new Mock<IImageProcessor>().Object, currentUser, TimeProvider.System,
            Options.Create(new ImageStorageOptions()));

    private static Application.Features.Images.UploadImage.Command UploadCommand()
        => new(new AppImageCreationDto());

    private static Mock<IAppUserRepository> UserRepository(AppUser user)
    {
        var repository = new Mock<IAppUserRepository>();
        repository.Setup(service => service.GetByIdAsync(user.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return repository;
    }

    private static Mock<IRepositoryManager> Repositories(Mock<IAppUserRepository> users)
    {
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.AppUser).Returns(users.Object);
        repositories.Setup(repository => repository.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repositories;
    }

    private static AppUser User(int id, bool canUploadImages, params AppUserRole[] roles) => new()
    {
        Id = id,
        Login = "target",
        NormalizedLogin = "TARGET",
        PasswordHash = "unused",
        Roles = roles.Length == 0 ? [AppUserRole.User] : [.. roles],
        CanUploadImages = canUploadImages,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class TestUser(int? userId, params AppUserRole[] roles) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => roles.Any(candidate => string.Equals(candidate.ToString(), role, StringComparison.Ordinal));
        public void RequireAuthenticated() { }
    }
}
