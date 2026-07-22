using Pgvector;

namespace Entities.Models;

public abstract class AppImage
{
    public int Id { get; set; }
    public ImageSource Source { get; set; }
    public int? ExternalMediaId { get; set; }
    public int? UploadedById { get; set; }
    public AppUser? UploadedBy { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public ImageVisibility Visibility { get; set; }
    public ModerationStatus ModerationStatus { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<ImageTag> Tags { get; set; } = [];
    public List<ImageTagChange> TagChanges { get; set; } = [];
    public List<Comment> Comments { get; set; } = [];
    public List<ImageLike> Likes { get; set; } = [];
    public Vector? Embedding { get; set; }
    public EmbeddingStatus EmbeddingStatus { get; set; }
    public int EmbeddingAttempts { get; set; }
    public string? EmbeddingError { get; set; }
    public DateTimeOffset? EmbeddingLeaseExpiresAtUtc { get; set; }
}

public enum ImageSource
{
    UserUpload,
    Scraped
}

public enum ImageVisibility
{
    Gallery,
    Private
}

public enum ModerationStatus
{
    Pending,
    Approved,
    Rejected
}

public enum EmbeddingStatus
{
    Pending,
    Processing,
    Ready,
    Failed
}
