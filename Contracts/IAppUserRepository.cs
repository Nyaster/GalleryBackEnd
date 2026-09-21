using Entities.Models;

namespace Contracts;

public interface IAppUserRepository
{
    // These methods require an active transaction. Always lock users before refresh sessions.
    Task<AppUser?> LockByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AppUser?> LockByNormalizedLoginAsync(string normalizedLogin, CancellationToken cancellationToken = default);
    Task RevokeAllRefreshSessionsAsync(int userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<AppUser?> GetByNormalizedLoginAsync(string normalizedLogin, bool trackChanges,
        CancellationToken cancellationToken = default);

    Task<AppUser?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken = default);

    Task<int?> GetAuthenticationVersionAsync(int id, CancellationToken cancellationToken = default);

    Task<RefreshSession?> GetRefreshSessionAsync(byte[] tokenHash, bool trackChanges,
        CancellationToken cancellationToken = default);

    Task AddAsync(AppUser user, CancellationToken cancellationToken = default);
    Task AddRefreshSessionAsync(RefreshSession session, CancellationToken cancellationToken = default);

    Task<RefreshSessionRotationResult> RotateRefreshSessionAsync(byte[] tokenHash, RefreshSession replacement,
        DateTimeOffset now, CancellationToken cancellationToken = default);

    Task PurgeExpiredRefreshSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}