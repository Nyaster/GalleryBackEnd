namespace Service.Contracts;

public interface IImageStager
{
    /// <summary>Inspects and stages an image, borrowing the caller-owned source stream.</summary>
    Task<IStagedImage> StageAsync(Stream source, string storageKeyPrefix,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns a staged file until its database record has been persisted. Disposal is idempotent;
/// unpersisted files are deleted best effort, independently of request cancellation.
/// Cleanup failures are logged and may leave an artifact; they do not replace the operation failure.
/// </summary>
public interface IStagedImage : IAsyncDisposable
{
    string StorageKey { get; }
    InspectedImage Metadata { get; }

    /// <summary>
    /// Makes the file permanent. Call immediately after a successful database save, before
    /// another await or source-stream disposal. This synchronous marker performs no I/O.
    /// </summary>
    void MarkPersisted();
}
