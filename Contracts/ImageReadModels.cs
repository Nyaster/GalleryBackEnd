using Entities.Models;

namespace Contracts;

public readonly record struct ImageViewer(int? UserId, bool IsStaff);

public sealed record ImageMetadata(
    int Id,
    int? UploadedById,
    ImageVisibility Visibility,
    ModerationStatus ModerationStatus,
    DateTimeOffset? DeletedAtUtc,
    string StorageKey,
    string ContentType,
    bool HasEmbedding);

public sealed record ImageTagSummary(string Name, TagModerationStatus ModerationStatus);

public sealed record ImageCard(
    int Id,
    ImageSource Source,
    int? UploadedById,
    string? UploadedBy,
    DateTimeOffset UploadedAtUtc,
    int Width,
    int Height,
    ImageVisibility Visibility,
    ModerationStatus ModerationStatus,
    AiUsageClassification AiUsage,
    DateTimeOffset? DeletedAtUtc,
    IReadOnlyList<ImageTagSummary> Tags,
    int LikeCount,
    int CommentCount,
    bool IsLikedByCurrentUser,
    bool CanSeePendingTags);
