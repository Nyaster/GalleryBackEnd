using Entities.Models;

namespace Contracts;

public interface IRepositoryManager
{
    IAppUserRepository AppUser { get; }
    IAppImageRepository AppImage { get; }
    Task<ScrapeRun?> GetScrapeRunAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<ScrapeRun?> ClaimNextScrapeRunAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task AddScrapeRunAsync(ScrapeRun run, CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
