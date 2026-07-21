namespace Shared.DataTransferObjects;

public sealed record PageableRecommendationsDto(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AppImageDto> Images);
