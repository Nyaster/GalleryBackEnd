using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class AppUserRepository(RepositoryContext context) : IAppUserRepository
{
    public Task<AppUser?> GetByNormalizedLoginAsync(string normalizedLogin, bool trackChanges, CancellationToken cancellationToken = default)
        => QueryUsers(trackChanges).SingleOrDefaultAsync(user => user.NormalizedLogin == normalizedLogin, cancellationToken);

    public Task<AppUser?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default)
        => QueryUsers(trackChanges).SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<RefreshSession?> GetRefreshSessionAsync(byte[] tokenHash, bool trackChanges, CancellationToken cancellationToken = default)
    {
        var query = trackChanges ? context.RefreshSessions : context.RefreshSessions.AsNoTracking();
        return query.Include(session => session.User)
            .SingleOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);
    }

    public Task AddAsync(AppUser user, CancellationToken cancellationToken = default)
        => context.AppUsers.AddAsync(user, cancellationToken).AsTask();

    public Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken = default)
        => context.RefreshSessions.AddAsync(session, cancellationToken).AsTask();

    public async Task<AppUser?> RotateRefreshSessionAsync(byte[] tokenHash, RefreshSession replacement, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // SELECT ... FOR UPDATE is also non-composable. Keep the locked entity tracked, then
        // load the user separately inside the same transaction.
        var session = context.RefreshSessions.FromSqlInterpolated($"""
            SELECT * FROM "RefreshSessions" WHERE "TokenHash" = {tokenHash} FOR UPDATE
            """).AsEnumerable().SingleOrDefault();
        if (session is null || session.ExpiresAtUtc <= now || session.RevokedAtUtc is not null)
        {
            if (session is not null)
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "RefreshSessions" SET "RevokedAtUtc" = {now}, "RevokeReason" = {"reuse detected"}
                    WHERE "FamilyId" = {session.FamilyId} AND "RevokedAtUtc" IS NULL
                    """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        session.RevokedAtUtc = now;
        session.RevokeReason = "rotated";
        session.ReplacedBySessionId = replacement.Id;
        replacement.FamilyId = session.FamilyId;
        replacement.UserId = session.UserId;
        var user = await context.AppUsers.SingleAsync(item => item.Id == session.UserId, cancellationToken);
        await context.RefreshSessions.AddAsync(replacement, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public Task PurgeExpiredRefreshSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => context.RefreshSessions.Where(session => session.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<AppUser> QueryUsers(bool trackChanges)
        => trackChanges ? context.AppUsers : context.AppUsers.AsNoTracking();
}
