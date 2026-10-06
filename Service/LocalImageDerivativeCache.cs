using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Service.Contracts;

namespace Service;

public sealed class LocalImageDerivativeCache(IImageStorage storage, ILogger<LocalImageDerivativeCache> logger)
    : IImageDerivativeCache, IDisposable
{
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
        if (storage.Exists(cacheStorageKey))
            return await storage.OpenReadAsync(cacheStorageKey, cancellationToken);

        var gate = RentGate(cacheStorageKey);
        var acquired = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            acquired = true;
            if (storage.Exists(cacheStorageKey))
                return await storage.OpenReadAsync(cacheStorageKey, cancellationToken);

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
                return await StoreBestEffortAsync(response, cacheStorageKey, sourceStorageKey, variant.CacheKey, cancellationToken);
            }
            catch
            {
                await (response ?? generated).DisposeAsync();
                throw;
            }
        }
        finally
        {
            if (acquired) gate.Semaphore.Release();
            ReturnGate(cacheStorageKey, gate);
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

    public void Dispose() => _conversionLimit.Dispose();
}
