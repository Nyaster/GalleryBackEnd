using Entities.Models;

namespace Service.Contracts;

public interface IImageParserService
{
    Task<ScrapeResult> RunAsync(ScrapeRun run, CancellationToken cancellationToken = default);
}

public sealed record ScrapeResult(int ImagesDiscovered, int ImagesImported, bool CompletedWithErrors, int FailedItems);
