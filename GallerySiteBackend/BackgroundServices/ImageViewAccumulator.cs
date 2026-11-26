using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using Contracts;
using Service.Contracts;

namespace Application.BackgroundService;

public sealed class ImageViewAccumulator : IImageViewRecorder, IDisposable
{
    private readonly TimeProvider _clock;
    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _flushSignal = new(0, 1);
    private readonly Meter _meter = new("GallerySite.ImageViews");
    private readonly Counter<long> _dropped;
    private readonly Histogram<double> _flushDuration;
    private Dictionary<int, long> _totals = new();
    private Dictionary<(int ImageId, int UserId), DateTimeOffset> _firstSeen = new();
    private long? _oldestActive;
    private long? _oldestInFlight;
    private long _eventCount;
    private bool _flushRequested;
    private bool _accepting = true;

    public ImageViewAccumulator(TimeProvider clock) : this(clock, 10_000)
    {
    }

    internal ImageViewAccumulator(TimeProvider clock, int capacity)
    {
        _clock = clock;
        _capacity = capacity;
        _dropped = _meter.CreateCounter<long>("gallery.image_views.dropped", "{view}");
        _flushDuration = _meter.CreateHistogram<double>("gallery.image_views.flush.duration", "ms");
        _meter.CreateObservableGauge("gallery.image_views.oldest_pending.age", OldestPendingAge, "s");
    }

    public void Record(int imageId, int? userId)
    {
        var observedAt = userId.HasValue ? _clock.GetUtcNow() : default;
        string reason;
        lock (_gate)
        {
            if (!_accepting)
            {
                reason = "shutdown";
            }
            else
            {
                var hasImage = _totals.TryGetValue(imageId, out var count);
                var pair = (imageId, userId.GetValueOrDefault());
                var earliest = default(DateTimeOffset);
                var hasPair = userId.HasValue && _firstSeen.TryGetValue(pair, out earliest);
                var addedSlots = (hasImage ? 0 : 1) + (userId.HasValue && !hasPair ? 1 : 0);
                var size = _totals.Count + _firstSeen.Count + addedSlots;
                if (size >= _capacity && !_flushRequested)
                {
                    _flushRequested = true;
                    _flushSignal.Release();
                }

                if (size <= _capacity)
                {
                    _totals[imageId] = count + 1;
                    if (userId.HasValue && (!hasPair || observedAt < earliest))
                        _firstSeen[pair] = observedAt;

                    _oldestActive ??= _clock.GetTimestamp();
                    _eventCount++;
                    return;
                }

                reason = "capacity";
            }
        }

        RecordDropped(1, reason);
    }

    internal async Task WaitForFlushAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(interval, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
        try
        {
            await _flushSignal.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested &&
                                                 !stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal PendingBatch? TakeBatch()
    {
        lock (_gate)
        {
            _flushRequested = false;
            _flushSignal.Wait(0);
            if (_totals.Count == 0)
                return null;

            var pending = new PendingBatch(new ImageViewBatch(
                    Guid.NewGuid(),
                    _clock.GetUtcNow(),
                    new ReadOnlyDictionary<int, long>(_totals),
                    new ReadOnlyDictionary<(int ImageId, int UserId), DateTimeOffset>(_firstSeen)),
                _eventCount);
            _totals = new();
            _firstSeen = new();
            _oldestInFlight = _oldestActive;
            _oldestActive = null;
            _eventCount = 0;
            return pending;
        }
    }

    internal void CompleteBatch()
    {
        lock (_gate)
            _oldestInFlight = null;
    }

    internal void StopAccepting()
    {
        lock (_gate)
            _accepting = false;
    }

    internal void RecordDropped(long count, string reason)
        => _dropped.Add(count, new KeyValuePair<string, object?>("reason", reason));

    internal void RecordFlush(TimeSpan duration, string outcome)
        => _flushDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));

    private double OldestPendingAge()
    {
        lock (_gate)
        {
            var oldest = _oldestInFlight ?? _oldestActive;
            return oldest.HasValue ? _clock.GetElapsedTime(oldest.Value).TotalSeconds : 0;
        }
    }

    public void Dispose() => _meter.Dispose();

    internal sealed record PendingBatch(ImageViewBatch Batch, long EventCount);
}