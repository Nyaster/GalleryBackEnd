using Application.BackgroundService;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ImageViewBufferTests
{
    [Fact]
    public void CapacityDropsTheWholeViewAndKeepsTheEarliestRegisteredTimestamp()
    {
        var clock = new TestViewClock();
        using var accumulator = new ImageViewAccumulator(clock, capacity: 3);
        accumulator.Record(1, null);
        accumulator.Record(1, 7);
        clock.UtcNow = clock.UtcNow.AddMinutes(-1);
        accumulator.Record(1, 7);
        accumulator.Record(2, 7);
        accumulator.Record(2, null);
        accumulator.Record(3, null);

        var pending = Assert.IsType<ImageViewAccumulator.PendingBatch>(accumulator.TakeBatch());
        Assert.Equal(4, pending.EventCount);
        Assert.Equal(3, pending.Batch.Totals[1]);
        Assert.Equal(1, pending.Batch.Totals[2]);
        Assert.Equal(2, pending.Batch.Totals.Count);
        var visit = Assert.Single(pending.Batch.FirstSeen);
        Assert.Equal((1, 7), visit.Key);
        Assert.Equal(clock.UtcNow, visit.Value);
    }

    [Fact]
    public void ConcurrentRepeatsStayOnOneImageAndOneRegisteredPair()
    {
        using var accumulator = new ImageViewAccumulator(TimeProvider.System, capacity: 2);
        accumulator.Record(1, 7);
        Parallel.For(0, 1_000, index => accumulator.Record(1, index % 2 == 0 ? 7 : null));

        var pending = Assert.IsType<ImageViewAccumulator.PendingBatch>(accumulator.TakeBatch());
        Assert.Equal(1_001, Assert.Single(pending.Batch.Totals).Value);
        Assert.Equal((1, 7), Assert.Single(pending.Batch.FirstSeen).Key);
    }

    [Fact]
    public async Task CapacitySignalsFlushAndShutdownDrainsTheActiveBuffer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var accumulator = new ImageViewAccumulator(TimeProvider.System, capacity: 2);
        var started = new TaskCompletionSource<ImageViewBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new List<ImageViewBatch>();
        await using var services = Services(batch =>
        {
            batches.Add(batch);
            started.TrySetResult(batch);
            return Task.CompletedTask;
        });
        using var worker = Worker(accumulator, services, TimeSpan.FromHours(1));
        await worker.StartAsync(deadline.Token);
        accumulator.Record(1, 7);
        var first = await started.Task.WaitAsync(deadline.Token);
        accumulator.Record(2, null);
        await worker.StopAsync(deadline.Token);

        Assert.Equal(2, batches.Count);
        Assert.Same(first, batches[0]);
        Assert.Equal(1, Assert.Single(batches[1].Totals).Value);
        Assert.True(batches[1].Totals.ContainsKey(2));
        Assert.NotEqual(first.Id, batches[1].Id);
    }

    [Fact]
    public async Task PeriodicFlushRetriesTheSameBatchAndLeavesLaterViewsForTheNextBatch()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var accumulator = new ImageViewAccumulator(TimeProvider.System, capacity: 4);
        var firstAttempt = new TaskCompletionSource<ImageViewBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt =
            new TaskCompletionSource<ImageViewBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new List<ImageViewBatch>();
        await using var services = Services(batch =>
        {
            batches.Add(batch);
            if (batches.Count == 1)
            {
                firstAttempt.SetResult(batch);
                throw new InvalidOperationException("Simulated failure after commit");
            }

            secondAttempt.TrySetResult(batch);
            return Task.CompletedTask;
        });
        using var worker = Worker(accumulator, services, TimeSpan.FromMilliseconds(50));
        accumulator.Record(1, 7);
        await worker.StartAsync(deadline.Token);
        var first = await firstAttempt.Task.WaitAsync(deadline.Token);
        accumulator.Record(1, 7);
        var retry = await secondAttempt.Task.WaitAsync(deadline.Token);
        await worker.StopAsync(deadline.Token);

        Assert.Same(first, retry);
        Assert.Equal(3, batches.Count);
        Assert.NotEqual(first.Id, batches[2].Id);
        Assert.Equal(1, first.Totals[1]);
        Assert.Equal(1, batches[2].Totals[1]);
    }

    private static ServiceProvider Services(Func<ImageViewBatch, Task> apply)
        => new ServiceCollection()
            .AddScoped<IImageViewRepository>(_ => new TestViewRepository(apply))
            .BuildServiceProvider();

    private static ImageViewFlushService Worker(ImageViewAccumulator accumulator, ServiceProvider services,
        TimeSpan interval)
        => new(accumulator, services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<ImageViewFlushService>.Instance, interval);

    private sealed class TestViewRepository(Func<ImageViewBatch, Task> apply) : IImageViewRepository
    {
        public Task ApplyAsync(ImageViewBatch batch, CancellationToken cancellationToken) => apply(batch);

        public Task<ImageViewTotals> GetTotalsAsync(int imageId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ImageViewDay>> GetDailyAsync(int imageId, DateOnly from, DateOnly through,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestViewClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}