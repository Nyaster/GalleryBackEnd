using Application.Features.Images.GetForMe;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using Service;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class ForMeFeedHandlerTests
{
    private readonly IForMeFeedCursorProtector _cursors = new ForMeFeedCursorProtector(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Handle_NewFeed_ClampsPageSizeNormalizesFiltersAndReturnsProtectedContinuation()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var repository = new Mock<IForMeFeedRepository>();
        repository.Setup(value => value.CreateAsync(7, 50, It.Is<ForMeFilters>(filters =>
                filters.ExcludedTags.SequenceEqual(new[] { "landscape" }) && filters.AiUsage.SequenceEqual(new[] { 1 })),
            It.IsAny<CancellationToken>())).ReturnsAsync(id);
        repository.Setup(value => value.ReadAsync(id, new ImageViewer(7, false), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForMeFeedPage(new ForMeFeed
            {
                Id = id, UserId = 7, Mode = "Personalized", PageSize = 50, Total = 80,
                CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1)
            }, [], 50, true));
        var handler = new Handler(repository.Object, _cursors, new User(7), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);

        var response = await handler.Handle(new Command(500, [AiUsageClassification.HumanMade],
            [" Landscape ", "landscape", " "]), CancellationToken.None);

        Assert.Equal(80, response.Total);
        var continuation = _cursors.Unprotect(response.NextCursor!);
        Assert.Equal(id, continuation.FeedId);
        Assert.Equal(7, continuation.UserId);
        Assert.Equal(50, continuation.LastRank);
    }

    [Fact]
    public async Task Handle_ForeignCursor_HidesFeedEvenWhenExpired()
    {
        var token = _cursors.Protect(new ForMeFeedCursor(1, Guid.NewGuid(), 8, 20, 20,
            DateTimeOffset.UtcNow.AddHours(-1)));
        var handler = new Handler(Mock.Of<IForMeFeedRepository>(), _cursors, new User(7), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);
        await Assert.ThrowsAsync<Base404ReturnException>(() => handler.Handle(new Command(Cursor: token), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExpiredOwnedCursor_ReturnsConflictInsteadOfCreatingFeed()
    {
        var token = _cursors.Protect(new ForMeFeedCursor(1, Guid.NewGuid(), 7, 20, 20,
            DateTimeOffset.UtcNow.AddHours(-1)));
        var handler = new Handler(Mock.Of<IForMeFeedRepository>(), _cursors, new User(7), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);
        await Assert.ThrowsAsync<Base409ConflictException>(() => handler.Handle(new Command(Cursor: token), CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tampered")]
    public void Unprotect_InvalidToken_RejectsInput(string token)
        => Assert.Throws<Base400BadRequestException>(() => _cursors.Unprotect(token));

    [Fact]
    public async Task Handle_Unauthenticated_RejectsRequest()
    {
        var handler = new Handler(Mock.Of<IForMeFeedRepository>(), _cursors, new User(null), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);
        await Assert.ThrowsAsync<Base401UnauthorizedException>(() => handler.Handle(new Command(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ServerDeadline_IsUnavailableAndDoesNotReadAPartialFeed()
    {
        var clock = new DeadlineClock();
        var repository = new Mock<IForMeFeedRepository>(MockBehavior.Strict);
        repository.Setup(value => value.CreateAsync(7, 20, It.IsAny<ForMeFilters>(), It.IsAny<CancellationToken>()))
            .Returns((int _, int _, ForMeFilters _, CancellationToken token) =>
            {
                clock.Fire();
                return Task.FromCanceled<Guid>(token);
            });
        var handler = new Handler(repository.Object, _cursors, new User(7), clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);
        await Assert.ThrowsAsync<ForMeFeedDeadlineException>(() => handler.Handle(new Command(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RequestCancellation_IsNotReportedAsServerDeadline()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<IForMeFeedRepository>(MockBehavior.Strict);
        repository.Setup(value => value.CreateAsync(7, 20, It.IsAny<ForMeFilters>(), It.IsAny<CancellationToken>()))
            .Returns((int _, int _, ForMeFilters _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<Guid>(token);
            });
        var handler = new Handler(repository.Object, _cursors, new User(7), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Handler>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handle(new Command(), cancellation.Token));
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new ManualTimer();
        }
        public void Fire() => _callback!(_state);
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class User(int? id) : IUserContext
    {
        public int? UserId => id;
        public string? Login => "test";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated()
        {
            if (id is null) throw new Base401UnauthorizedException("Authentication required.");
        }
    }
}
