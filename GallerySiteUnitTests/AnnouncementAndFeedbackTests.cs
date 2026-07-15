using Application.Features.Announcements;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Service.Contracts;
using Shared.DataTransferObjects;
using System.Runtime.CompilerServices;

namespace GallerySiteUnitTests;

public sealed class AnnouncementAndFeedbackTests
{
    [Theory]
    [InlineData(typeof(AnnouncementController), "Get", "GET", null)]
    [InlineData(typeof(FeedbackController), "Create", "POST", null)]
    [InlineData(typeof(AdministrationController), "UpdateAnnouncement", "PUT", "announcement")]
    [InlineData(typeof(AdministrationController), "ClearAnnouncement", "DELETE", "announcement")]
    [InlineData(typeof(AdministrationController), "Feedback", "GET", "feedback")]
    public void Routes_ExposeExpectedHttpContracts(Type controllerType, string methodName, string verb, string? template)
    {
        var method = controllerType.GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var route = method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single();

        Assert.Equal(template, route.Template);
        Assert.Contains(verb, route.HttpMethods);
    }

    [Theory]
    [InlineData("UpdateAnnouncement")]
    [InlineData("ClearAnnouncement")]
    [InlineData("Feedback")]
    public void AdministrationEndpoints_AreAdminOnly(string methodName)
    {
        var method = typeof(AdministrationController).GetMethod(methodName) ?? throw new InvalidOperationException($"{methodName} was not found.");
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public void FeedbackCreate_UsesDedicatedRateLimitPolicy()
    {
        var method = typeof(FeedbackController).GetMethod("Create") ?? throw new InvalidOperationException("Create was not found.");
        var rateLimit = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>().Single();
        Assert.Equal("feedback-write", rateLimit.PolicyName);
    }

    [Fact]
    public async Task GetAnnouncement_WithoutActiveAnnouncement_ReturnsEmptyShape()
    {
        var repository = new Mock<IAnnouncementRepository>();
        repository.Setup(item => item.GetAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync((Announcement?)null);
        var handler = new GetAnnouncementHandler(Repositories(announcements: repository.Object).Object, new TestUser(8));

        var result = await handler.Handle(new GetAnnouncementCommand(), CancellationToken.None);

        Assert.Null(result.Content);
        Assert.Null(result.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAnnouncement_CreateAndReplace_UsesTrimmedContentTimestampAndAdmin()
    {
        var announcementRepository = new Mock<IAnnouncementRepository>();
        var stored = new StrongBox<Announcement?>();
        announcementRepository.Setup(item => item.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(() => stored.Value);
        announcementRepository.Setup(item => item.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Callback<Announcement, CancellationToken>((item, _) => stored.Value = item).Returns(Task.CompletedTask);
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var handler = new UpdateAnnouncementHandler(Repositories(announcements: announcementRepository.Object).Object, new TestUser(3, true), new FixedClock(now));

        var created = await handler.Handle(new UpdateAnnouncementCommand(new UpdateAnnouncementDto("  First notice  ")), CancellationToken.None);
        var replaced = await handler.Handle(new UpdateAnnouncementCommand(new UpdateAnnouncementDto("Second notice")), CancellationToken.None);

        Assert.Equal("First notice", created.Content);
        Assert.Equal(now, created.UpdatedAtUtc);
        Assert.Equal(1, stored.Value!.Id);
        Assert.Equal("Second notice", replaced.Content);
        Assert.Equal(3, stored.Value.UpdatedByUserId);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task UpdateAnnouncement_InvalidContent_ThrowsBadRequest(string? content)
    {
        var handler = new UpdateAnnouncementHandler(Repositories().Object, new TestUser(3, true), TimeProvider.System);
        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(new UpdateAnnouncementCommand(new UpdateAnnouncementDto(content)), CancellationToken.None));
    }

    [Fact]
    public async Task CreateFeedback_BlankOrOverLimitContent_ThrowsBadRequest()
    {
        var handler = new CreateFeedbackHandler(Repositories().Object, new TestUser(3), TimeProvider.System);

        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(new CreateFeedbackCommand(new CreateFeedbackDto(" \t ")), CancellationToken.None));
        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(new CreateFeedbackCommand(new CreateFeedbackDto(new string('x', 2001))), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAnnouncement_NonAdmin_ThrowsForbidden()
    {
        var handler = new UpdateAnnouncementHandler(Repositories().Object, new TestUser(3), TimeProvider.System);
        await Assert.ThrowsAsync<AppForbiddenException>(() => handler.Handle(new UpdateAnnouncementCommand(new UpdateAnnouncementDto("notice")), CancellationToken.None));
    }

    [Fact]
    public async Task ClearAnnouncement_WithoutActiveAnnouncement_IsIdempotent()
    {
        var announcementRepository = new Mock<IAnnouncementRepository>();
        announcementRepository.Setup(item => item.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync((Announcement?)null);
        var repositories = Repositories(announcements: announcementRepository.Object);
        var handler = new ClearAnnouncementHandler(repositories.Object, new TestUser(3, true));

        await handler.Handle(new ClearAnnouncementCommand(), CancellationToken.None);

        repositories.Verify(item => item.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateFeedback_AssignsAuthorTrimmedContentAndTimestamp()
    {
        var feedbackRepository = new Mock<IFeedbackRepository>();
        feedbackRepository.Setup(item => item.AddAsync(It.IsAny<Feedback>(), It.IsAny<CancellationToken>()))
            .Callback<Feedback, CancellationToken>((item, _) => item.Id = 42).Returns(Task.CompletedTask);
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var handler = new CreateFeedbackHandler(Repositories(feedback: feedbackRepository.Object).Object, new TestUser(12, login: "alice"), new FixedClock(now));

        var result = await handler.Handle(new CreateFeedbackCommand(new CreateFeedbackDto("  Helpful site.  ")), CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal(12, result.AuthorId);
        Assert.Equal("alice", result.AuthorLogin);
        Assert.Equal("Helpful site.", result.Content);
        Assert.Equal(now, result.CreatedAtUtc);
    }

    [Fact]
    public async Task GetFeedback_ClampsPaginationAndReturnsNewestFirstRepositoryResults()
    {
        var feedbackRepository = new Mock<IFeedbackRepository>();
        var newest = new Feedback { Id = 2, AuthorId = 7, Author = User("new"), Content = "new", CreatedAtUtc = DateTimeOffset.MaxValue };
        var oldest = new Feedback { Id = 1, AuthorId = 6, Author = User("old"), Content = "old", CreatedAtUtc = DateTimeOffset.MinValue };
        feedbackRepository.Setup(item => item.GetAsync(1, 50, It.IsAny<CancellationToken>())).ReturnsAsync(([newest, oldest], 2));
        var handler = new GetFeedbackHandler(Repositories(feedback: feedbackRepository.Object).Object, new TestUser(3, true));

        var result = await handler.Handle(new GetFeedbackCommand(0, 99), CancellationToken.None);

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal([2, 1], result.Feedback.Select(item => item.Id));
    }

    private static Mock<IRepositoryManager> Repositories(IAnnouncementRepository? announcements = null, IFeedbackRepository? feedback = null)
    {
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(item => item.Announcements).Returns(announcements ?? new Mock<IAnnouncementRepository>().Object);
        repositories.SetupGet(item => item.Feedback).Returns(feedback ?? new Mock<IFeedbackRepository>().Object);
        repositories.Setup(item => item.SaveAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repositories;
    }

    private static AppUser User(string login) => new() { Login = login, NormalizedLogin = login, PasswordHash = "hash" };

    private sealed class TestUser(int userId, bool isAdmin = false, string? login = null) : IUserContext
    {
        public int? UserId => userId;
        public string? Login => login ?? "user";
        public bool IsInRole(string role) => isAdmin && role == nameof(AppUserRole.Admin);
        public void RequireAuthenticated() { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
