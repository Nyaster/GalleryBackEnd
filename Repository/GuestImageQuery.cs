using System.Linq.Expressions;
using Entities.Models;

namespace Repository;

internal static class GuestImageQuery
{
    public static readonly Expression<Func<AppImage, bool>> IsEligible = image =>
        image.Source == ImageSource.UserUpload &&
        image.Visibility == ImageVisibility.Gallery &&
        image.ModerationStatus == ModerationStatus.Approved &&
        image.DeletedAtUtc == null &&
        image.Tags.Any(tag => tag.NormalizedName == "sfw" &&
                              tag.ModerationStatus == TagModerationStatus.Approved) &&
        !image.Tags.Any(tag => tag.NormalizedName == "nsfw") &&
        !image.TagChanges.Any(change => change.Status == ImageTagChangeStatus.Pending &&
                                        change.ProposedTags.Contains("nsfw"));
}
