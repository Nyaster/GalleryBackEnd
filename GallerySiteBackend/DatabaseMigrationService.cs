using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Repository;
using Entities.Models;
using Service;

namespace GallerySiteBackend;

public static class DatabaseMigrationService
{
    private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromMinutes(10);

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var bootstrapAdmin = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapAdminOptions>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        logger.LogInformation("Applying pending database migrations");
        database.Database.SetCommandTimeout(MigrationCommandTimeout);
        await database.Database.MigrateAsync(cancellationToken);
        await SeedBootstrapAdminAsync(database, bootstrapAdmin.Value, passwordHasher, logger, cancellationToken);
    }

    private static async Task SeedBootstrapAdminAsync(RepositoryContext database, BootstrapAdminOptions configured,
        IPasswordHasher<AppUser> passwordHasher, ILogger logger, CancellationToken cancellationToken)
    {
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
