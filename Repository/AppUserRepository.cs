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

    private IQueryable<AppUser> QueryUsers(bool trackChanges)
        => trackChanges ? context.AppUsers : context.AppUsers.AsNoTracking();
}
