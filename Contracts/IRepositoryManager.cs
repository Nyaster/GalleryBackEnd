using Entities.Models;

namespace Contracts;

public interface IRepositoryManager
{
    IAppUserRepository AppUser { get; }
    IAppImageRepository AppImage { get; }
    IImageTagChangeRepository ImageTagChanges { get; }
    IInteractionRepository Interactions { get; }
    IRankingRepository Rankings { get; }
    IAnnouncementRepository Announcements { get; }
    IFeedbackRepository Feedback { get; }
    Task<ScrapeRun?> GetScrapeRunAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default);
    Task<ScrapeRun?> ClaimNextScrapeRunAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task AddScrapeRunAsync(ScrapeRun run, CancellationToken cancellationToken = default);
    Task<IRepositoryTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
