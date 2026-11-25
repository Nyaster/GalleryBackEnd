using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;
using Repository;
using Entities.Models;
using Service;
using GallerySiteBackend.Configuration;

namespace GallerySiteBackend;

public static class DatabaseMigrationService
{
    public static async Task MigrateAsync(IServiceProvider services, ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var bootstrapAdmin = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapAdminOptions>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseMigrationOptions>>().Value;
        logger.LogInformation("Applying pending database migrations");
        var previousTimeout = database.Database.GetCommandTimeout();
        try
        {
            database.Database.SetCommandTimeout(options.CommandTimeoutSeconds);
            await database.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            database.Database.SetCommandTimeout(previousTimeout);
        }

        await SeedBootstrapAdminAsync(database, bootstrapAdmin.Value, passwordHasher, logger, cancellationToken);
    }

    private static async Task SeedBootstrapAdminAsync(RepositoryContext database, BootstrapAdminOptions configured,
        IPasswordHasher<AppUser> passwordHasher, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configured.Login) && string.IsNullOrWhiteSpace(configured.Password)) return;
        if (string.IsNullOrWhiteSpace(configured.Login) || string.IsNullOrWhiteSpace(configured.Password))
            throw new InvalidOperationException(
                "BootstrapAdmin requires both Login and Password when either is configured.");
        var normalized = configured.Login.Trim().ToUpperInvariant();
        await using var transaction = await RoleAccessService.AcquireWriteLockAsync(database, cancellationToken);
        var existing = await database.AppUsers.Where(user => user.NormalizedLogin == normalized)
            .Select(user => (bool?)database.AccountRoles.Any(membership =>
                membership.UserId == user.Id && membership.RoleId == AccessRole.AdminId))
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (!existing.Value)
            {
                if (!await database.AccountRoles.AnyAsync(membership => membership.RoleId == AccessRole.AdminId,
                        cancellationToken))
                    throw new InvalidOperationException(
                        "BootstrapAdmin login belongs to an existing account without the Admin role. Configure an unused login to create the first administrator.");
                logger.LogWarning(
                    "Skipping bootstrap administrator creation because the configured login belongs to an existing account without the Admin role");
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

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
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.UniqueViolation,
                                                      ConstraintName: "UX_AppUsers_NormalizedLogin"
                                                  })
        {
            throw new InvalidOperationException("BootstrapAdmin login was claimed by another account.", exception);
        }

        database.AccountRoles.Add(new AccountRole { UserId = user.Id, RoleId = AccessRole.AdminId });
        database.AccessAudits.Add(new AccessAudit
        {
            Action = "bootstrapAdmin",
            TargetUserId = user.Id,
            RoleId = AccessRole.AdminId,
            After = JsonSerializer.Serialize(new[] { AccessRole.AdminId, AccessRole.UserId }),
            CreatedAtUtc = user.CreatedAtUtc
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Created bootstrap administrator {Login}", user.Login);
    }
}