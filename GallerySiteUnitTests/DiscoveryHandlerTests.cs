using Application.Features.Discovery;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Moq;
using Service.Contracts;

namespace GallerySiteUnitTests;

public sealed class DiscoveryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 23, 59, 59, TimeSpan.Zero);

    [Theory]
    [InlineData("most-liked", DiscoveryCollection.MostLiked)]
    [InlineData("new-uploads", DiscoveryCollection.NewUploads)]
    [InlineData("most-commented", DiscoveryCollection.MostCommented)]
    public async Task GetDiscovery_MidnightCrossing_CapturesTimeOnceAndUsesUtcDay(string name,
        DiscoveryCollection collection)
    {
        var clock = new CrossingMidnightClock();
        var discovery = new Mock<IDiscoveryRepository>(MockBehavior.Strict);
        var filters = new[] { AiUsageClassification.HumanMade };
        using var cancellation = new CancellationTokenSource();
        discovery.Setup(repository => repository.GetCollectionAsync(collection, Now.UtcDateTime.Date,
                Now.UtcDateTime.Date.AddDays(1),
                7, 1, 50, filters, cancellation.Token))
            .ReturnsAsync(new DiscoveryPage(9, 2, []));

        var result = await Handler(discovery, clock).Handle(new GetDiscoveryCommand(name, Page: 0, PageSize: 500,
            AiUsage: filters), cancellation.Token);

        Assert.Equal(1, clock.Calls);
        Assert.Equal(name, result.Collection);
        Assert.Equal("Daily", result.Period);
        Assert.Equal(new DateOnly(2026, 10, 4), result.Date);
        Assert.Equal("UTC", result.TimeZone);
        Assert.Equal(Now.UtcDateTime, result.GeneratedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.PeriodStartUtc.Kind);
        Assert.Equal(Now.UtcDateTime.Date, result.PeriodStartUtc);
        Assert.Equal(Now.UtcDateTime.Date.AddDays(1), result.PeriodEndUtc);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(9, result.GalleryTotal);
        Assert.Equal(2, result.Total);
        discovery.VerifyAll();
    }

    [Fact]
    public async Task GetDiscovery_PastDateAndSmallPageSize_UsesRequestedDayAndClampsSize()
    {
        var discovery = new Mock<IDiscoveryRepository>(MockBehavior.Strict);
        var start = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero);
        discovery.Setup(repository => repository.GetCollectionAsync(DiscoveryCollection.NewUploads, start,
                start.AddDays(1), 7, int.MaxValue, 1, null, CancellationToken.None))
            .ReturnsAsync(new DiscoveryPage(10, 3, []));

        var result = await Handler(discovery).Handle(new GetDiscoveryCommand("new-uploads", "2026-02-28",
            int.MaxValue, -5), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 2, 28), result.Date);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(3, result.Total);
        Assert.Empty(result.Entries);
        discovery.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2026-10-05")]
    [InlineData("2026-02-29")]
    [InlineData("2026-13-01")]
    [InlineData("2026-1-01")]
    [InlineData("2026-10-04T00:00:00Z")]
    [InlineData("04/10/2026")]
    [InlineData(" 2026-10-04")]
    [InlineData("9999-12-31")]
    public async Task GetDiscovery_InvalidOrFutureDate_RejectsBeforeReading(string date)
    {
        var discovery = new Mock<IDiscoveryRepository>(MockBehavior.Strict);
        await Assert.ThrowsAsync<Base400BadRequestException>(() => Handler(discovery).Handle(
            new GetDiscoveryCommand("most-liked", date), CancellationToken.None));
        discovery.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("MostLiked")]
    [InlineData("MOST-LIKED")]
    [InlineData("")]
    public async Task GetDiscovery_InvalidCollection_RejectsBeforeReading(string collection)
    {
        var discovery = new Mock<IDiscoveryRepository>(MockBehavior.Strict);
        await Assert.ThrowsAsync<Base400BadRequestException>(() => Handler(discovery).Handle(
            new GetDiscoveryCommand(collection), CancellationToken.None));
        discovery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetDiscovery_Unauthenticated_RejectsBeforeReading()
    {
        var repositories = new Mock<IRepositoryManager>(MockBehavior.Strict);
        var user = new Mock<IUserContext>();
        user.Setup(context => context.RequireAuthenticated())
            .Throws(new AppUserUnauthorizedException("Authentication required."));
        var handler = new GetDiscoveryHandler(repositories.Object, user.Object, new CrossingMidnightClock());

        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => handler.Handle(
            new GetDiscoveryCommand("new-uploads"), CancellationToken.None));
        repositories.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetDiscovery_FailedRead_PropagatesFailure()
    {
        var discovery = new Mock<IDiscoveryRepository>();
        discovery.Setup(repository => repository.GetCollectionAsync(DiscoveryCollection.MostLiked,
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), 7, 1, 20, null, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(discovery).Handle(
            new GetDiscoveryCommand("most-liked"), CancellationToken.None));
    }

    private static GetDiscoveryHandler Handler(Mock<IDiscoveryRepository> discovery, TimeProvider? clock = null)
    {
        var repositories = new Mock<IRepositoryManager>();
        repositories.SetupGet(repository => repository.Discovery).Returns(discovery.Object);
        var user = new Mock<IUserContext>();
        user.SetupGet(context => context.UserId).Returns(7);
        return new GetDiscoveryHandler(repositories.Object, user.Object, clock ?? new CrossingMidnightClock());
    }

    private sealed class CrossingMidnightClock : TimeProvider
    {
        public int Calls { get; private set; }
        public override DateTimeOffset GetUtcNow() => Now.AddDays(Calls++);
    }
}