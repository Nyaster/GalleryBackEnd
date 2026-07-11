using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class RepositoryManager(RepositoryContext context) : IRepositoryManager
{
    private readonly Lazy<IAppImageRepository> _images = new(() => new AppImageRepository(context));
    private readonly Lazy<IAppUserRepository> _users = new(() => new AppUserRepository(context));

    public IAppUserRepository AppUser => _users.Value;
    public IAppImageRepository AppImage => _images.Value;

    public Task<ScrapeRun?> GetScrapeRunAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default)
    {
        var query = trackChanges ? context.ScrapeRuns : context.ScrapeRuns.AsNoTracking();
        return query.SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<ScrapeRun?> ClaimNextScrapeRunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var run = await context.ScrapeRuns.Where(candidate => candidate.Status == BackgroundJobStatus.Queued)
            .OrderBy(candidate => candidate.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (run is null) return null;
        run.Status = BackgroundJobStatus.Running;
        run.StartedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }

    public Task AddScrapeRunAsync(ScrapeRun run, CancellationToken cancellationToken = default)
        => context.ScrapeRuns.AddAsync(run, cancellationToken).AsTask();

    public Task SaveAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
