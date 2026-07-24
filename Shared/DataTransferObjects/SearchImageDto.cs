using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record SearchImageDto(
    IReadOnlyList<string>? Tags,
    ImageKind Kind = ImageKind.All,
    ImageSort Sort = ImageSort.Newest,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyList<AiUsageClassification>? AiUsage = null,
    string? RandomSeed = null,
    IReadOnlyList<string>? ExcludedTags = null);

public enum ImageKind { All, Official, Fan }
public enum ImageSort { Newest, Oldest, MediaId, Random }
