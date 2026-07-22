namespace Entities.Models;

public sealed class ImageTagChange
{
    public long Id { get; set; }
    public int ImageId { get; set; }
    public AppImage? Image { get; set; }
    public required List<string> PreviousTags { get; set; }
    public required List<string> ProposedTags { get; set; }
    public ImageTagChangeKind Kind { get; set; }
    public ImageTagChangeStatus Status { get; set; }
    public int? EditedByUserId { get; set; }
    public required string EditedByLogin { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? ReviewedByLogin { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public int? RevertedByUserId { get; set; }
    public string? RevertedByLogin { get; set; }
    public DateTimeOffset? RevertedAtUtc { get; set; }
    public string? ReversionNote { get; set; }
}

public enum ImageTagChangeKind
{
    Replacement,
    GlobalTagRejection
}

public enum ImageTagChangeStatus
{
    Pending,
    Approved,
    Rejected,
    Reverted
}
