using System.Text.Json.Serialization;

namespace Shared.DataTransferObjects;

public sealed record DiscoveryEntryDto(
    AppImageDto Image,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PeriodLikeCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PeriodCommentCount = null);

public sealed record PageableDiscoveryDto(
    string Collection,
    string Period,
    DateOnly Date,
    string TimeZone,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime GeneratedAtUtc,
    int GalleryTotal,
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<DiscoveryEntryDto> Entries);
