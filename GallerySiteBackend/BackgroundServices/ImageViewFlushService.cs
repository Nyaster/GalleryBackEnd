using Contracts;

namespace Application.BackgroundService;

public sealed class ImageViewFlushService : Microsoft.Extensions.Hosting.BackgroundService
{
    private static readonly TimeSpan MaxBatchAge = TimeSpan.FromHours(24);
    private readonly ImageViewAccumulator _accumulator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ImageViewFlushService> _logger;
    private readonly TimeSpan _flushInterval;
    private ImageViewAccumulator.PendingBatch? _pending;

    public ImageViewFlushService(ImageViewAccumulator accumulator, IServiceScopeFactory scopeFactory,
        TimeProvider clock, ILogger<ImageViewFlushService> logger)
        : this(accumulator, scopeFactory, clock, logger, TimeSpan.FromSeconds(10))
    {
    }

    internal ImageViewFlushService(ImageViewAccumulator accumulator, IServiceScopeFactory scopeFactory,
        TimeProvider clock, ILogger<ImageViewFlushService> logger, TimeSpan flushInterval)
    {
        _accumulator = accumulator;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
        _flushInterval = flushInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var flushStartedAt = _clock.GetTimestamp();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_pending is null)
            {
                var remaining = _flushInterval - _clock.GetElapsedTime(flushStartedAt);
                if (remaining > TimeSpan.Zero)
                    await _accumulator.WaitForFlushAsync(remaining, stoppingToken);
            }
            else
            {
                await Task.Delay(_flushInterval, _clock, stoppingToken);
            }

            flushStartedAt = _clock.GetTimestamp();
            await FlushAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _accumulator.StopAccepting();
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is { IsCompleted: false })
            return;

        while (await FlushAsync(cancellationToken))
        {
        }

        _pending ??= _accumulator.TakeBatch();
        while (_pending is { } batch)
        {
            _accumulator.RecordDropped(batch.EventCount, "shutdown");
            CompleteBatch();
            _pending = _accumulator.TakeBatch();
        }
    }

    private async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        _pending ??= _accumulator.TakeBatch();
        if (_pending is not { } pending)
            return false;

        if (_clock.GetUtcNow() - pending.Batch.CreatedAtUtc >= MaxBatchAge)
        {
            _accumulator.RecordDropped(pending.EventCount, "expired");
            _logger.LogWarning("Image view batch {BatchId} expired before it could be confirmed", pending.Batch.Id);
            CompleteBatch();
            return true;
        }

        var startedAt = _clock.GetTimestamp();
        var outcome = "success";
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IImageViewRepository>();
            await repository.ApplyAsync(pending.Batch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            return false;
        }
        catch (Exception exception)
        {
            outcome = "error";
            _logger.LogWarning(exception, "Could not confirm image view batch {BatchId}", pending.Batch.Id);
            return false;
        }
        finally
        {
            _accumulator.RecordFlush(_clock.GetElapsedTime(startedAt), outcome);
        }

        CompleteBatch();
        return true;
    }

    private void CompleteBatch()
    {
        _pending = null;
        _accumulator.CompleteBatch();
    }
}