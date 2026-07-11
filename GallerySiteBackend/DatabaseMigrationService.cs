using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Repository;
using Entities.Models;
using Service;

namespace GallerySiteBackend;

public sealed class DatabaseMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrationService> logger,
    IOptions<BootstrapAdminOptions> bootstrapAdmin,
    IPasswordHasher<AppUser> passwordHasher) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        logger.LogInformation("Applying pending database migrations");
        await database.Database.MigrateAsync(cancellationToken);
        await SeedBootstrapAdminAsync(database, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedBootstrapAdminAsync(RepositoryContext database, CancellationToken cancellationToken)
    {
        var configured = bootstrapAdmin.Value;
        if (string.IsNullOrWhiteSpace(configured.Login) && string.IsNullOrWhiteSpace(configured.Password)) return;
        if (string.IsNullOrWhiteSpace(configured.Login) || string.IsNullOrWhiteSpace(configured.Password))
            throw new InvalidOperationException("BootstrapAdmin requires both Login and Password when either is configured.");
        var normalized = configured.Login.Trim().ToUpperInvariant();
        if (await database.AppUsers.AnyAsync(user => user.NormalizedLogin == normalized, cancellationToken)) return;
        var user = new AppUser
        {
            Login = configured.Login.Trim(),
            NormalizedLogin = normalized,
            PasswordHash = string.Empty,
            Roles = [AppUserRole.Admin, AppUserRole.User],
            CreatedAtUtc = TimeProvider.System.GetUtcNow()
        };
        user.PasswordHash = passwordHasher.HashPassword(user, configured.Password);
        database.AppUsers.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created bootstrap administrator {Login}", user.Login);
    }
}
