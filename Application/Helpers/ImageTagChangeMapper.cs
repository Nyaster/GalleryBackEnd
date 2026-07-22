using Entities.Models;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageTagChangeMapper
{
    public static List<string> Snapshot(IEnumerable<ImageTag> tags)
        => tags.Select(tag => tag.NormalizedName).Distinct(StringComparer.Ordinal).OrderBy(tag => tag, StringComparer.Ordinal).ToList();

    public static bool SameSnapshot(IEnumerable<string> left, IEnumerable<string> right)
        => left.OrderBy(tag => tag, StringComparer.Ordinal).SequenceEqual(right.OrderBy(tag => tag, StringComparer.Ordinal), StringComparer.Ordinal);

    public static ImageTagChangeDto ToDto(ImageTagChange change)
    {
        var previous = change.PreviousTags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
        var proposed = change.ProposedTags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
        return new ImageTagChangeDto(
            change.Id,
            change.ImageId,
            change.Kind,
            change.Status,
            change.EditedByUserId,
            change.EditedByLogin,
            change.CreatedAtUtc,
            previous,
            proposed,
            proposed.Except(previous, StringComparer.Ordinal).ToArray(),
            previous.Except(proposed, StringComparer.Ordinal).ToArray(),
            change.AppliedAtUtc,
            change.ReviewedByUserId,
            change.ReviewedByLogin,
            change.ReviewedAtUtc,
            change.ReviewNote,
            change.RevertedByUserId,
            change.RevertedByLogin,
            change.RevertedAtUtc,
            change.ReversionNote);
    }

    public static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
