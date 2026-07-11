namespace Service.Contracts;

public interface IImageStorage
{
    Task<string> SaveTemporaryAsync(Stream source, CancellationToken cancellationToken = default);
    Task MoveTemporaryToFinalAsync(string temporaryPath, string storageKey, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    bool Exists(string storageKey);
}
