using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class AppUserRepository(RepositoryContext context) : IAppUserRepository
{
    public async Task<AppUser?> LockByIdAsync(int id, CancellationToken cancellationToken = default)
        => (await context.AppUsers.FromSqlInterpolated($"SELECT * FROM \"AppUsers\" WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<AppUser?> LockByNormalizedLoginAsync(string normalizedLogin,
        CancellationToken cancellationToken = default)
        => (await context.AppUsers
            .FromSqlInterpolated($"SELECT * FROM \"AppUsers\" WHERE \"NormalizedLogin\" = {normalizedLogin} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public Task RevokeAllRefreshSessionsAsync(int userId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => context.RefreshSessions.Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, now)
                .SetProperty(session => session.RevokeReason, "account recovery"), cancellationToken);

    public Task<AppUser?> GetByNormalizedLoginAsync(string normalizedLogin, bool trackChanges,
        CancellationToken cancellationToken = default)
        => QueryUsers(trackChanges)
            .SingleOrDefaultAsync(user => user.NormalizedLogin == normalizedLogin, cancellationToken);

    public Task<AppUser?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default)
        => QueryUsers(trackChanges).SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<int?> GetAuthenticationVersionAsync(int id, CancellationToken cancellationToken = default)
        => context.AppUsers.Where(user => user.Id == id)
            .Select(user => (int?)user.AuthenticationVersion).SingleOrDefaultAsync(cancellationToken);

    public Task<RefreshSession?> GetRefreshSessionAsync(byte[] tokenHash, bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        var query = trackChanges ? context.RefreshSessions : context.RefreshSessions.AsNoTracking();
        return query.Include(session => session.User)
            .SingleOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);
    }

    public Task AddAsync(AppUser user, CancellationToken cancellationToken = default)
        => context.AppUsers.AddAsync(user, cancellationToken).AsTask();

    public Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken = default)
        => context.RefreshSessions.AddAsync(session, cancellationToken).AsTask();

    public async Task<RefreshSessionRotationResult> RotateRefreshSessionAsync(byte[] tokenHash,
        RefreshSession replacement, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Discover the owner without a lock, then lock the user before the session. Recovery
        // uses the same order, so rotation cannot escape revocation or deadlock with it.
        var userId = await context.RefreshSessions.AsNoTracking().Where(item => item.TokenHash == tokenHash)
            .Select(item => (int?)item.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
            return new RefreshSessionRotationResult(null, null, false);
        var user = await LockByIdAsync(userId.Value, cancellationToken);
        if (user is null)
            return new RefreshSessionRotationResult(null, userId, false);
        // Materialize before selecting so EF does not compose over the locking query.
        var session = (await context.RefreshSessions.FromSqlInterpolated($"""
                                                                          SELECT * FROM "RefreshSessions" WHERE "TokenHash" = {tokenHash} FOR UPDATE
                                                                          """).ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (session is null || session.ExpiresAtUtc <= now || session.RevokedAtUtc is not null)
        {
            if (session is not null)
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                                                                    UPDATE "RefreshSessions" SET "RevokedAtUtc" = {now}, "RevokeReason" = {"reuse detected"}
                                                                    WHERE "FamilyId" = {session.FamilyId} AND "RevokedAtUtc" IS NULL
                                                                    """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshSessionRotationResult(null, session?.UserId, session is not null);
        }

        session.RevokedAtUtc = now;
        session.RevokeReason = "rotated";
        session.ReplacedBySessionId = replacement.Id;
        replacement.FamilyId = session.FamilyId;
        replacement.UserId = session.UserId;
        await context.RefreshSessions.AddAsync(replacement, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RefreshSessionRotationResult(user, user.Id, false);
    }

    public Task PurgeExpiredRefreshSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => context.RefreshSessions.Where(session => session.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<AppUser> QueryUsers(bool trackChanges)
        => trackChanges ? context.AppUsers : context.AppUsers.AsNoTracking();
}