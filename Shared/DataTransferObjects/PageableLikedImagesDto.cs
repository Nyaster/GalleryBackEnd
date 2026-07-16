namespace Shared.DataTransferObjects;

public sealed record PageableLikedImagesDto(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AppImageDto> Images);
