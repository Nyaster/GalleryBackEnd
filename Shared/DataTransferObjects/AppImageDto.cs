using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record AppImageDto(
    int Id,
    ImageSource Source,
    string? UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string ContentUrl,
    IReadOnlyList<string> Tags,
    int Width,
    int Height,
    ImageVisibility Visibility,
    ModerationStatus ModerationStatus,
    IReadOnlyList<string>? PendingTags = null,
    DateTimeOffset? HiddenAtUtc = null);
