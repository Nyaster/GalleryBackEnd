using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record StartScrapeDto(ScrapeMode Mode);
public sealed record ScrapeRunDto(Guid Id, ScrapeMode Mode, BackgroundJobStatus Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, int ImagesDiscovered, int ImagesImported, string? Error,
    bool CompletedWithErrors = false, int FailedItems = 0);
