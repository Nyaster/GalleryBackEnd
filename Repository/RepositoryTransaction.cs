using Contracts;
using Microsoft.EntityFrameworkCore.Storage;

namespace Repository;

internal sealed class RepositoryTransaction(IDbContextTransaction transaction) : IRepositoryTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default)
        => transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default)
        => transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
