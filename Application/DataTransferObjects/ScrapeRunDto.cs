using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record StartScrapeDto(ScrapeMode Mode, int? MaxImages = null);
public sealed record ScrapeRunDto(Guid Id, ScrapeMode Mode, BackgroundJobStatus Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, int ImagesDiscovered, int ImagesImported, string? Error,
    bool CompletedWithErrors = false, int FailedItems = 0, int MaxImages = 0, int TotalPages = 0, int ScannedPages = 0,
    int EligibleCandidates = 0, int PlannedDownloads = 0, int ProcessedDownloads = 0, DateTimeOffset? LastProgressAtUtc = null);
