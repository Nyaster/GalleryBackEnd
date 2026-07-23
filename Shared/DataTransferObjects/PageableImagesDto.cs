using System.Text.Json.Serialization;

namespace Shared.DataTransferObjects;

public sealed record PageableImagesDto(
    int Page,
    int PageSize,
    int Total,
    ImageSort Sort,
    IReadOnlyList<AppImageDto> Images,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RandomSeed = null);
