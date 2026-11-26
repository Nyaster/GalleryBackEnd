using System.Text.Json.Serialization;
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
    AiUsageClassification AiUsage,
    IReadOnlyList<string>? PendingTags = null,
    DateTimeOffset? HiddenAtUtc = null,
    int LikeCount = 0,
    int CommentCount = 0,
    bool IsLikedByCurrentUser = false,
    bool CanHide = false,
    bool CanRestore = false,
    bool CanPublish = false,
    bool CanEditTags = false,
    bool CanViewTagHistory = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalViews { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? UniqueRegisteredViewers { get; init; }
}