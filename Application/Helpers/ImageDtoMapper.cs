using Entities.Models;
using Shared.DataTransferObjects;

namespace Application.Helpers;

internal static class ImageDtoMapper
{
    public static AppImageDto ToDto(AppImage image) => new(
        image.Id,
        image.Source,
        image.UploadedBy?.Login,
        image.UploadedAtUtc,
        $"/api/images/{image.Id}/content",
        image.Tags.OrderBy(tag => tag.Name).Select(tag => tag.Name).ToArray(),
        image.Width,
        image.Height,
        image.Visibility,
        image.ModerationStatus);
}
