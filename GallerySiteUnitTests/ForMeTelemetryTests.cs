using Application.Features.Images.GetForMe;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class ForMeTelemetryTests
{
    [Fact]
    public async Task Handle_UnexpectedReadFailure_RecordsOneSafeFailureWithPartialReadTiming()
    {
        var logs = new RecordingLogger<Handler>();
        var id = Guid.NewGuid();
        var repository = new Mock<IForMeFeedRepository>();
        repository.Setup(value => value.CreateAsync(7, 20, It.IsAny<ForMeFilters>(), It.IsAny<CancellationToken>())).ReturnsAsync(id);
        var failure = new InvalidOperationException("exception-secret");
        repository.Setup(value => value.ReadAsync(id, new ImageViewer(7, false), 0, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var handler = new Handler(repository.Object, Mock.Of<IForMeFeedCursorProtector>(), new User(), TimeProvider.System, logs);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new Command(ExcludedTags: ["tag-secret"]), default)));
        var entry = Assert.Single(logs.Events);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("ForMeRequestHandled", entry.Fields["EventName"]);
        Assert.Equal("Failed", entry.Fields["Outcome"]);
        Assert.Equal("ReadFeedPage", entry.Fields["Stage"]);
        Assert.IsType<double>(entry.Fields["CreateFeedElapsedMilliseconds"]);
        Assert.IsType<double>(entry.Fields["ReadFeedPageElapsedMilliseconds"]);
        Assert.False(entry.Fields.ContainsKey("Mode"));
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("secret", string.Join(',', entry.Fields.Values));
    }

    [Fact]
    public async Task Handle_PostReadCursorMismatch_IsRejectedAtConsistencyStage()
    {
        var logs = new RecordingLogger<Handler>();
        var cursor = new ForMeFeedCursor(1, Guid.NewGuid(), 7, 1, 20, DateTimeOffset.UtcNow.AddHours(1));
        var protector = new Mock<IForMeFeedCursorProtector>();
        protector.Setup(value => value.Unprotect("cursor-secret")).Returns(cursor);
        var repository = new Mock<IForMeFeedRepository>();
        repository.Setup(value => value.ReadAsync(cursor.FeedId, new ImageViewer(7, false), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForMeFeedPage(new ForMeFeed { PageSize = 1, ExpiresAtUtc = cursor.ExpiresAtUtc }, [], 2, false));
        var handler = new Handler(repository.Object, protector.Object, new User(), TimeProvider.System, logs);
        await Assert.ThrowsAsync<Base400BadRequestException>(() => handler.Handle(new Command(Cursor: "cursor-secret"), default));
        var entry = Assert.Single(logs.Events);
        Assert.Equal("ValidateCursorConsistency", entry.Fields["Stage"]);
        Assert.Equal("Rejected", entry.Fields["Outcome"]);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.False(entry.Fields.ContainsKey("CreateFeedElapsedMilliseconds"));
        Assert.False(entry.Fields.ContainsKey("Mode"));
    }

    [Fact]
    public async Task Handle_AuthenticationRejected_DoesNotInventDurationsOrResultFields()
    {
        var logs = new RecordingLogger<Handler>();
        var handler = new Handler(Mock.Of<IForMeFeedRepository>(), Mock.Of<IForMeFeedCursorProtector>(),
            new User(false), TimeProvider.System, logs);
        await Assert.ThrowsAsync<Base401UnauthorizedException>(() => handler.Handle(new Command(), default));
        var entry = Assert.Single(logs.Events);
        Assert.Equal("Authenticate", entry.Fields["Stage"]);
        Assert.Equal("Rejected", entry.Fields["Outcome"]);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.False(entry.Fields.ContainsKey("CreateFeedElapsedMilliseconds"));
        Assert.False(entry.Fields.ContainsKey("ReadFeedPageElapsedMilliseconds"));
        Assert.False(entry.Fields.ContainsKey("Mode"));
    }

    [Fact]
    public async Task Handle_CanceledRead_PreservesExceptionAndPartialTiming()
    {
        var logs = new RecordingLogger<Handler>();
        var id = Guid.NewGuid();
        var repository = new Mock<IForMeFeedRepository>();
        repository.Setup(value => value.CreateAsync(7, 20, It.IsAny<ForMeFilters>(), It.IsAny<CancellationToken>())).ReturnsAsync(id);
        var cancellation = new OperationCanceledException("cancel-secret");
        repository.Setup(value => value.ReadAsync(id, new ImageViewer(7, false), 0, It.IsAny<CancellationToken>())).ThrowsAsync(cancellation);
        var handler = new Handler(repository.Object, Mock.Of<IForMeFeedCursorProtector>(), new User(), TimeProvider.System, logs);
        Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() => handler.Handle(new Command(), default)));
        var entry = Assert.Single(logs.Events);
        Assert.Equal("Canceled", entry.Fields["Outcome"]);
        Assert.Equal("ReadFeedPage", entry.Fields["Stage"]);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<double>(entry.Fields["ReadFeedPageElapsedMilliseconds"]);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("cancel-secret", string.Join(',', entry.Fields.Values));
    }

    private sealed class User(bool authenticated = true) : IUserContext
    {
        public int? UserId => authenticated ? 7 : null;
        public string? Login => "user-secret";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated()
        {
            if (!authenticated) throw new Base401UnauthorizedException("user-secret");
        }
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, Dictionary<string, object?> Fields, Exception? Exception)> Events { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Events.Add((logLevel, ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
            .ToDictionary(pair => pair.Key, pair => pair.Value), exception));
}
