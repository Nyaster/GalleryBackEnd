using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class RepositoryManager(RepositoryContext context) : IRepositoryManager
{
    private readonly Lazy<IAppImageRepository> _images = new(() => new AppImageRepository(context));
    private readonly Lazy<IAppUserRepository> _users = new(() => new AppUserRepository(context));
    private readonly Lazy<IInteractionRepository> _interactions = new(() => new InteractionRepository(context));
    private readonly Lazy<IRankingRepository> _rankings = new(() => new RankingRepository(context));

    public IAppUserRepository AppUser => _users.Value;
    public IAppImageRepository AppImage => _images.Value;
    public IInteractionRepository Interactions => _interactions.Value;
    public IRankingRepository Rankings => _rankings.Value;

    public Task<ScrapeRun?> GetScrapeRunAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default)
    {
        var query = trackChanges ? context.ScrapeRuns : context.ScrapeRuns.AsNoTracking();
        return query.SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<ScrapeRun?> ClaimNextScrapeRunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var leaseExpires = now.AddMinutes(30);
        // UPDATE ... RETURNING is deliberately non-composable PostgreSQL SQL. Materialize it
        // before selecting the sole returned row so EF does not append LIMIT/OFFSET to it.
        return context.ScrapeRuns.FromSqlInterpolated($"""
            UPDATE "ScrapeRuns" SET "Status" = {"Running"}, "StartedAtUtc" = {now}, "LeaseExpiresAtUtc" = {leaseExpires},
                "Attempts" = "Attempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "ScrapeRuns"
                WHERE "Status" = {"Queued"} OR ("Status" = {"Running"} AND "LeaseExpiresAtUtc" < {now})
                ORDER BY "CreatedAtUtc" FOR UPDATE SKIP LOCKED LIMIT 1
            ) RETURNING *
            """).AsEnumerable().SingleOrDefault();
    }

    public Task AddScrapeRunAsync(ScrapeRun run, CancellationToken cancellationToken = default)
        => context.ScrapeRuns.AddAsync(run, cancellationToken).AsTask();

    public Task SaveAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
