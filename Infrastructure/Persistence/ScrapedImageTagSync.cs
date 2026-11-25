using System.Data;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Repository;

namespace Infrastructure.Persistence;

internal static class ScrapedImageTagSync
{
    private const string Placeholder = "none yet";
    private const int TagLimit = 20;

    internal static string[] SourceTags(IEnumerable<string> names) => names
        .Select(name => name.Trim().ToLowerInvariant())
        .Where(name => name.Length is > 0 and <= 64 && name != Placeholder).Distinct().Take(TagLimit).ToArray();

    internal static bool NeedsMerge(AppImage image, string[] sourceTags)
    {
        var current = image.Tags.Select(tag => tag.NormalizedName).ToHashSet(StringComparer.Ordinal);
        return (current.Contains(Placeholder) && current.Count > 1) ||
               (current.Count(name => name != Placeholder) < TagLimit &&
                sourceTags.Any(name => !current.Contains(name)));
    }

    // The caller supplies a fresh context per image. A skipped transaction must never be flushed by a progress save.
    internal static async Task<bool> TrySynchronizeAsync(RepositoryContext database, int imageId, string[] sourceTags,
        DateTimeOffset uploadedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction =
            await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var image = await database.SelebusImages.Include(row => row.Tags)
                .SingleOrDefaultAsync(row => row.Id == imageId, cancellationToken);
            if (image is null) return false;
            if (NeedsMerge(image, sourceTags))
                await MergeTagsAsync(database, image, sourceTags, now, cancellationToken);
            image.UploadedAtUtc = uploadedAt;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception error) when (IsWriteConflict(error))
        {
            return false;
        }
    }

    private static async Task MergeTagsAsync(RepositoryContext database, AppImage image, string[] sourceTags,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var history = await database.ImageTagChanges.Where(change => change.ImageId == image.Id)
            .OrderByDescending(change => change.AppliedAtUtc).ThenByDescending(change => change.Id)
            .Select(change => new { change.Kind, change.Status, change.PreviousTags, change.ProposedTags })
            .ToListAsync(cancellationToken);
        // Approval and Revert both compare the complete active tag snapshot.
        if (history.Any(change => change.Status == ImageTagChangeStatus.Pending)) return;
        var current = image.Tags.Select(tag => tag.NormalizedName).ToHashSet(StringComparer.Ordinal);
        var latest = history.FirstOrDefault(change => change.Status == ImageTagChangeStatus.Approved);
        if (latest?.Kind == ImageTagChangeKind.Replacement && current.SetEquals(latest.ProposedTags) &&
            !await database.Tags.AnyAsync(tag => latest.PreviousTags.Contains(tag.NormalizedName) &&
                                                 tag.ModerationStatus == TagModerationStatus.Rejected,
                cancellationToken))
            return;

        var excluded = new HashSet<string>(current, StringComparer.Ordinal);
        foreach (var decision in history)
        {
            // Respect applied removals, and additions that a reviewer rejected or reverted.
            excluded.UnionWith(decision.Status == ImageTagChangeStatus.Approved
                ? decision.PreviousTags.Except(decision.ProposedTags)
                : decision.ProposedTags.Except(decision.PreviousTags));
        }

        var candidates = sourceTags.Where(name => !excluded.Contains(name)).ToArray();
        if (candidates.Length > 0)
        {
            // Check rejected names before creating new tags so skipped additions leave no unused tag rows.
            var rejected = await database.Tags.Where(tag => candidates.Contains(tag.NormalizedName) &&
                                                            tag.ModerationStatus == TagModerationStatus.Rejected)
                .Select(tag => tag.NormalizedName).ToListAsync(cancellationToken);
            var additions = candidates.Except(rejected).Take(TagLimit - current.Count(name => name != Placeholder))
                .ToArray();
            if (additions.Length > 0)
                image.Tags.AddRange(
                    await new AppImageRepository(database).GetOrCreateTagsAsync(additions, now, cancellationToken));
        }

        if (image.Tags.Any(tag =>
                tag.NormalizedName != Placeholder && tag.ModerationStatus != TagModerationStatus.Rejected))
            image.Tags.RemoveAll(tag => tag.NormalizedName == Placeholder);
    }

    internal static bool IsWriteConflict(Exception error) => error is DbUpdateConcurrencyException ||
                                                             error.GetBaseException() is PostgresException
                                                             {
                                                                 SqlState: PostgresErrorCodes.SerializationFailure
                                                                 or PostgresErrorCodes.DeadlockDetected
                                                                 or PostgresErrorCodes.UniqueViolation
                                                             };
}