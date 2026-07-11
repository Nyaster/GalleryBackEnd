namespace Shared.DataTransferObjects;

public sealed record PageableImagesDto(
    int Page,
    int PageSize,
    int Total,
    ImageSort Sort,
    IReadOnlyList<AppImageDto> Images);
