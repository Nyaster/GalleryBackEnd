using Microsoft.Extensions.Diagnostics.HealthChecks;
using Repository;

namespace GallerySiteBackend;

public sealed class DatabaseHealthCheck(RepositoryContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database is reachable.")
            : HealthCheckResult.Unhealthy("Database is unreachable.");
}
