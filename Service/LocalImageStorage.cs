using Microsoft.Extensions.Options;
using Service.Contracts;

namespace Service;

public sealed class LocalImageStorage(IOptions<ImageStorageOptions> options) : IImageStorage
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveTemporaryAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var tempDirectory = Path.Combine(_rootPath, ".tmp");
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.upload");
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public Task MoveTemporaryToFinalAsync(string temporaryPath, string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = ToFullPath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(temporaryPath, destination, false);
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(ToFullPath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ToFullPath(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool Exists(string storageKey) => File.Exists(ToFullPath(storageKey));

    private string ToFullPath(string storageKey)
    {
        var path = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        if (!path.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid image storage key.");
        return path;
    }
}
