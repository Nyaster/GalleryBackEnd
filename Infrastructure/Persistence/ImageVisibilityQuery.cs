using Entities.Models;
using Repository;

namespace Infrastructure.Persistence;

internal static class ImageVisibilityQuery
{
    public static IQueryable<AppImage> ApplyViewerVisibility(this IQueryable<AppImage> images,
        RepositoryContext context, int? viewerId)
    {
        var optedInViewers = context.AppUsers.Where(user => user.Id == viewerId && user.DisplayAllContent);
        return images.Where(image =>
            (viewerId != null && optedInViewers.Any()) ||
            !context.ContentVisibilityRules.Any(rule => rule.TagLinks.Any() &&
                                                        rule.TagLinks.All(link => image.Tags.Any(tag =>
                                                            tag.Id == link.TagId &&
                                                            tag.ModerationStatus == TagModerationStatus.Approved))));
    }
}
