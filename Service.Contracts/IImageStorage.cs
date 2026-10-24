namespace Service.Contracts;

public interface IImageStorage
{
    Task<string> SaveTemporaryAsync(Stream source, CancellationToken cancellationToken = default);
    /// <summary>Deletes a path owned by temporary storage; a missing file is harmless.</summary>
    Task DeleteTemporaryAsync(string temporaryPath, CancellationToken cancellationToken = default);

    // A failed move must not acquire an existing destination. The local adapter uses a non-overwriting move.
    Task MoveTemporaryToFinalAsync(string temporaryPath, string storageKey,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    bool Exists(string storageKey);
}