namespace Shared.DataTransferObjects;

public sealed record ForMeFeedDto(
    string Mode,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int PageSize,
    int Total,
    IReadOnlyList<AppImageDto> Images,
    string? NextCursor);
