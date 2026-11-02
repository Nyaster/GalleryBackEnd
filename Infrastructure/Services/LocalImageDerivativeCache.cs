using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Service.Contracts;

namespace Service;

public sealed class LocalImageDerivativeCache(IImageStorage storage, ILogger<LocalImageDerivativeCache> logger,
    IOptions<ImageStorageOptions> options)
    : IImageDerivativeCache, IDisposable
{
    private const int MaximumMemoryEntryBytes = 1024 * 1024;
    private readonly long _memoryBudgetBytes = options.Value.PreviewMemoryCacheMegabytes * 1024L * 1024L;
    private readonly MemoryCache? _memoryCache = options.Value.PreviewMemoryCacheMegabytes == 0
        ? null
        : new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.PreviewMemoryCacheMegabytes * 1024L * 1024L });
    private readonly SemaphoreSlim _admissionLimit = new(2, 2);
    private readonly SemaphoreSlim _conversionLimit = new(2, 2);
    private readonly Lock _gateLock = new();
    private readonly Dictionary<string, ConversionGate> _conversionGates = new(StringComparer.Ordinal);

    public async Task<Stream> GetOrCreateAsync(
        string sourceStorageKey,
        ImageDerivativeVariant variant,
        Func<Stream, CancellationToken, Task<Stream>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceStorageKey);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(factory);

        var cacheStorageKey = CreateCacheStorageKey(sourceStorageKey, variant);
        cancellationToken.ThrowIfCancellationRequested();
        var useMemory = _memoryCache is not null && variant == ImageDerivativeVariants.PreviewJpeg768V1;
        if (useMemory && TryOpenMemory(cacheStorageKey, out var cached))
            return cached!;
        if (!useMemory && storage.Exists(cacheStorageKey))
            return await storage.OpenReadAsync(cacheStorageKey, cancellationToken);

        var gate = RentGate(cacheStorageKey);
        var acquired = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            acquired = true;
            if (useMemory && TryOpenMemory(cacheStorageKey, out cached))
                return cached!;
            if (storage.Exists(cacheStorageKey))
            {
                var candidate = await storage.OpenReadAsync(cacheStorageKey, cancellationToken);
                return useMemory
                    ? await AdmitAsync(candidate, cacheStorageKey, cancellationToken)
                    : candidate;
            }

            Stream generated;
            await _conversionLimit.WaitAsync(cancellationToken);
            try
            {
                await using var source = await storage.OpenReadAsync(sourceStorageKey, cancellationToken);
                // Converters can do synchronous CPU work before returning a task.
                generated = await Task.Run(() => factory(source, cancellationToken), cancellationToken);
            }
            finally
            {
                _conversionLimit.Release();
            }
            Stream? response = null;
            try
            {
                response = await EnsureSeekableAsync(generated, cancellationToken);
                await StoreBestEffortAsync(response, cacheStorageKey, sourceStorageKey, variant.CacheKey, cancellationToken);
            }
            catch
            {
                await (response ?? generated).DisposeAsync();
                throw;
            }
            return useMemory
                ? await AdmitAsync(response, cacheStorageKey, cancellationToken)
                : response;
        }
        finally
        {
            if (acquired) gate.Semaphore.Release();
            ReturnGate(cacheStorageKey, gate);
        }
    }

    private bool TryOpenMemory(string key, out Stream? stream)
    {
        stream = _memoryCache!.TryGetValue(key, out byte[]? bytes) && bytes is not null
            ? new MemoryStream(bytes, writable: false)
            : null;
        return stream is not null;
    }

    private async Task<Stream> AdmitAsync(Stream candidate, string key, CancellationToken cancellationToken)
    {
        try
        {
            if (!candidate.CanSeek)
                return candidate;
            long length;
            try
            {
                length = candidate.Length;
            }
            catch (NotSupportedException)
            {
                return candidate;
            }
            candidate.Position = 0;
            if (length == 0 || length > Math.Min(_memoryBudgetBytes, MaximumMemoryEntryBytes) ||
                !_admissionLimit.Wait(0))
                return candidate;

            try
            {
                var bytes = new byte[(int)length];
                await candidate.ReadExactlyAsync(bytes, cancellationToken);
                candidate.Position = 0;
                cancellationToken.ThrowIfCancellationRequested();
                _memoryCache!.Set(key, bytes, new MemoryCacheEntryOptions
                {
                    Size = bytes.Length,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                });
                await candidate.DisposeAsync();
                return new MemoryStream(bytes, writable: false);
            }
            finally
            {
                _admissionLimit.Release();
            }
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }
    }

    private ConversionGate RentGate(string cacheStorageKey)
    {
        lock (_gateLock)
        {
            if (!_conversionGates.TryGetValue(cacheStorageKey, out var gate))
            {
                gate = new ConversionGate();
                _conversionGates.Add(cacheStorageKey, gate);
            }
            gate.Users++;
            return gate;
        }
    }

    private void ReturnGate(string cacheStorageKey, ConversionGate gate)
    {
        lock (_gateLock)
        {
            if (--gate.Users == 0)
            {
                _conversionGates.Remove(cacheStorageKey);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class ConversionGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private async Task<Stream> StoreBestEffortAsync(Stream generated, string cacheStorageKey, string sourceStorageKey,
        string variantKey, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            generated.Position = 0;
            temporaryPath = await storage.SaveTemporaryAsync(generated, cancellationToken);
            generated.Position = 0;

            try
            {
                await storage.MoveTemporaryToFinalAsync(temporaryPath, cacheStorageKey, cancellationToken);
                temporaryPath = null;
            }
            catch (IOException) when (storage.Exists(cacheStorageKey))
            {
                // Another process won the atomic write race. This generated stream is still a valid response.
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ImageDerivativeCacheWriteFailed {SourceStorageKey} {VariantKey}",
                sourceStorageKey, variantKey);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "ImageDerivativeTemporaryFileDeleteFailed {TemporaryPath}", temporaryPath);
                }
            }
            generated.Position = 0;
        }

        return generated;
    }

    private static async Task<Stream> EnsureSeekableAsync(Stream generated, CancellationToken cancellationToken)
    {
        if (generated.CanSeek)
            return generated;

        var buffered = new MemoryStream();
        try
        {
            await generated.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            await generated.DisposeAsync();
            return buffered;
        }
        catch
        {
            await buffered.DisposeAsync();
            throw;
        }
    }

    private static string CreateCacheStorageKey(string sourceStorageKey, ImageDerivativeVariant variant)
    {
        if (string.IsNullOrWhiteSpace(variant.CacheKey) || variant.CacheKey.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("The derivative cache key contains invalid characters.", nameof(variant));
        if (string.IsNullOrWhiteSpace(variant.Extension) || variant.Extension.Length < 2 || variant.Extension[0] != '.' ||
            variant.Extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("The derivative extension is invalid.", nameof(variant));

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceStorageKey)));
        return Path.Combine(".derivatives", variant.CacheKey, hash[..2], $"{hash}{variant.Extension}");
    }

    public void Dispose()
    {
        _memoryCache?.Dispose();
        _admissionLimit.Dispose();
        _conversionLimit.Dispose();
    }
}
