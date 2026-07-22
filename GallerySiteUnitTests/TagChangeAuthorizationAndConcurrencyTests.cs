using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class TagChangeAuthorizationAndConcurrencyTests
{
    [Fact]
    public async Task GetImageTagChanges_Owner_CanReadHistory()
    {
        var result = await GetHistory(new TestUser(7));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetImageTagChanges_Staff_CanReadHistory()
    {
        var result = await GetHistory(new TestUser(9, AppUserRole.Moderator));

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetImageTagChanges_UnrelatedUser_IsForbidden()
    {
        await Assert.ThrowsAsync<AppForbiddenException>(() => GetHistory(new TestUser(8)));
    }

    [Fact]
    public async Task GetAdminTagChanges_NonStaffCaller_IsForbidden()
    {
        var handler = new Application.Features.Administration.GetImageTagChanges.Handler(new Mock<IRepositoryManager>().Object, new TestUser(7));

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(
            new Application.Features.Administration.GetImageTagChanges.Command(null, null, null, 1, 20), CancellationToken.None));
    }

    [Fact]
    public async Task ModerateTagChange_NonStaffCaller_IsForbiddenBeforeStartingTransaction()
    {
        var repositories = new Mock<IRepositoryManager>();
        var handler = new Application.Features.Administration.ModerateImageTagChange.Handler(repositories.Object, new TestUser(7), TimeProvider.System);

        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(
            new Application.Features.Administration.ModerateImageTagChange.Command(1, TagChangeDecision.Approve, null), CancellationToken.None));

        repositories.Verify(value => value.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceTags_SaveFailure_RollsBackWithoutCommit()
    {
        var image = Image(7);
        var (repositories, images, changes, transaction) = TransactionalRepositories(image);
        images.Setup(value => value.GetOrCreateTagsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Tag("new")]);
        changes.Setup(value => value.AddAsync(It.IsAny<ImageTagChange>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repositories.Setup(value => value.SaveAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("save failed"));
        var handler = new Application.Features.Images.ReplaceImageTags.Handler(repositories.Object, new TestUser(7), TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.Handle(
            new Application.Features.Images.ReplaceImageTags.Command(image.Id, new ReplaceImageTagsDto(["new"])), CancellationToken.None));

        transaction.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApprovalRacingWithRejection_SerializationFailureRollsBackAndDoesNotReportSuccess()
    {
        var image = Image(7, Tag("old"));
        var change = Change(image.Id, ["old"], ["new"]);
        var (repositories, images, changes, transaction) = TransactionalRepositories(image);
        changes.Setup(value => value.GetByIdAsync(change.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(change);
        images.Setup(value => value.GetOrCreateTagsAsync(change.ProposedTags, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([Tag("new")]);
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(
            new PostgresException("serialization failure", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure));
        var handler = new Application.Features.Administration.ModerateImageTagChange.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        await Assert.ThrowsAsync<PostgresException>(() => handler.Handle(
            new Application.Features.Administration.ModerateImageTagChange.Command(change.Id, TagChangeDecision.Approve, null), CancellationToken.None));

        transaction.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GlobalRejectionRacingWithApproval_CommitFailureRollsBackAndDoesNotReportSuccess()
    {
        var tag = Tag("blocked");
        var image = Image(7, tag);
        tag.AppImages.Add(image);
        var pending = Change(image.Id, ["safe"], ["blocked"]);
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var changes = new Mock<IImageTagChangeRepository>();
        var transaction = Transaction(repositories);
        repositories.SetupGet(value => value.AppImage).Returns(images.Object);
        repositories.SetupGet(value => value.ImageTagChanges).Returns(changes.Object);
        repositories.Setup(value => value.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        images.Setup(value => value.GetTagByIdAsync(tag.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(tag);
        changes.Setup(value => value.GetPendingContainingTagAsync(tag.NormalizedName, It.IsAny<CancellationToken>())).ReturnsAsync([pending]);
        changes.Setup(value => value.AddRangeAsync(It.IsAny<IEnumerable<ImageTagChange>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("concurrent approval"));
        var handler = new Application.Features.Administration.ChangeTagModeration.Handler(repositories.Object, new TestUser(9, AppUserRole.Admin), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new Application.Features.Administration.ChangeTagModeration.Command(tag.Id, TagModerationStatus.Rejected), CancellationToken.None));

        transaction.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SerializationFailure_ReturnsConflictWithRetryMessage()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new PostgresException("serialization failure", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure)
        });
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, NullLoggerFactory.Instance);

        await handler.TryHandleAsync(context, context.Features.Get<IExceptionHandlerFeature>()!.Error, CancellationToken.None);

        context.Response.Body.Position = 0;
        var problem = await System.Text.Json.JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Contains("Refetch", problem!.Detail);
    }

    private static async Task<PageableImageTagChangesDto> GetHistory(IUserContext user)
    {
        var image = Image(7);
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var changes = new Mock<IImageTagChangeRepository>();
        repositories.SetupGet(value => value.AppImage).Returns(images.Object);
        repositories.SetupGet(value => value.ImageTagChanges).Returns(changes.Object);
        images.Setup(value => value.GetByIdAsync(image.Id, false, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        changes.Setup(value => value.GetAsync(image.Id, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ImageTagChange> { Change(image.Id, [], ["new"]) }, 1));
        var handler = new Application.Features.Images.GetImageTagChanges.Handler(repositories.Object, user);

        return await handler.Handle(new Application.Features.Images.GetImageTagChanges.Command(image.Id, 1, 20), CancellationToken.None);
    }

    private static (Mock<IRepositoryManager> Repositories, Mock<IAppImageRepository> Images, Mock<IImageTagChangeRepository> Changes, Mock<IRepositoryTransaction> Transaction) TransactionalRepositories(AppImage image)
    {
        var repositories = new Mock<IRepositoryManager>();
        var images = new Mock<IAppImageRepository>();
        var changes = new Mock<IImageTagChangeRepository>();
        var transaction = Transaction(repositories);
        repositories.SetupGet(value => value.AppImage).Returns(images.Object);
        repositories.SetupGet(value => value.ImageTagChanges).Returns(changes.Object);
        repositories.Setup(value => value.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        images.Setup(value => value.GetByIdAsync(image.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(image);
        changes.Setup(value => value.GetPendingForImageAsync(image.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ImageTagChange?)null);
        return (repositories, images, changes, transaction);
    }

    private static Mock<IRepositoryTransaction> Transaction(Mock<IRepositoryManager> repositories)
    {
        var transaction = new Mock<IRepositoryTransaction>();
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repositories.Setup(value => value.BeginSerializableTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        return transaction;
    }

    private static AppImage Image(int ownerId, params ImageTag[] tags) => new UserMadeImage
    {
        Id = 1,
        Source = ImageSource.UserUpload,
        UploadedById = ownerId,
        UploadedAtUtc = DateTimeOffset.UtcNow,
        Visibility = ImageVisibility.Gallery,
        ModerationStatus = ModerationStatus.Approved,
        StorageKey = "uploads/test.jpg",
        ContentType = "image/jpeg",
        Width = 1,
        Height = 1,
        EmbeddingStatus = EmbeddingStatus.Pending,
        Tags = tags.ToList()
    };

    private static ImageTag Tag(string name) => new()
    {
        Id = name == "old" ? 1 : 2,
        Name = name,
        NormalizedName = name,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ModerationStatus = TagModerationStatus.Approved
    };

    private static ImageTagChange Change(int imageId, IReadOnlyList<string> previous, IReadOnlyList<string> proposed) => new()
    {
        Id = 2,
        ImageId = imageId,
        PreviousTags = previous.ToList(),
        ProposedTags = proposed.ToList(),
        Kind = ImageTagChangeKind.Replacement,
        Status = ImageTagChangeStatus.Pending,
        EditedByUserId = 7,
        EditedByLogin = "owner",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed class TestUser(int userId, params AppUserRole[] roles) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => "tester";
        public bool IsInRole(string role) => roles.Any(candidate => candidate.ToString() == role);
        public void RequireAuthenticated() { }
    }
}
