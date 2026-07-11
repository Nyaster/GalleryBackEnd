using Entities.Models;

namespace Service.Contracts;

public interface IImageParserService
{
    Task<ScrapeResult> RunAsync(ScrapeMode mode, CancellationToken cancellationToken = default);
}

public sealed record ScrapeResult(int ImagesDiscovered, int ImagesImported);
