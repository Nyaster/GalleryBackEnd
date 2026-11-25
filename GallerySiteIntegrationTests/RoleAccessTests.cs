using System.Text.Json;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Service;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class RoleAccessMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MigrationPreservesLegacyStaffAndAssignsUserToEveryAccount()
    {
        const string previousMigration = "20261121180457_AddContentVisibility";
        await using var database = fixture.CreateContext();
        await database.GetService<IMigrator>().MigrateAsync(previousMigration);

        var admin = Account("legacy-admin",
            [AppUserRole.Admin, AppUserRole.Admin, AppUserRole.Moderator, AppUserRole.User]);
        var moderator = Account("legacy-mod", [AppUserRole.Moderator]);
        var empty = Account("legacy-empty", []);
        var unknown = Account("legacy-unknown", [(AppUserRole)999, AppUserRole.User]);
        database.AppUsers.AddRange(admin, moderator, empty, unknown);
        await database.SaveChangesAsync();
        await database.Database.MigrateAsync();

        var roles = new RoleAccessService(database, TimeProvider.System);
        Assert.Equal(new[] { "Admin", "Moderator", "User" }, (await roles.GetAccessAsync(admin.Id, default))!.Roles);
        Assert.Equal(new[] { "Moderator", "User" }, (await roles.GetAccessAsync(moderator.Id, default))!.Roles);
        Assert.Equal(new[] { "User" }, (await roles.GetAccessAsync(empty.Id, default))!.Roles);
        Assert.Equal(new[] { "User" }, (await roles.GetAccessAsync(unknown.Id, default))!.Roles);
        Assert.Equal(
            AccessPermission.AllKeys.Where(key => AccessPermission.HasLegacyPermission("Admin", key))
                .Order(StringComparer.Ordinal),
            (await roles.GetAccessAsync(admin.Id, default))!.Permissions);
        Assert.Equal(
            AccessPermission.AllKeys.Where(key => AccessPermission.HasLegacyPermission("Moderator", key))
                .Order(StringComparer.Ordinal),
            (await roles.GetAccessAsync(moderator.Id, default))!.Permissions);

        var createdAfterMigration = Account("new-user", [AppUserRole.User]);
        database.AppUsers.Add(createdAfterMigration);
        await database.SaveChangesAsync();
        Assert.Equal(new[] { "User" }, (await roles.GetAccessAsync(createdAfterMigration.Id, default))!.Roles);
        Assert.Empty((await roles.GetAccessAsync(createdAfterMigration.Id, default))!.Permissions);

        await database.GetService<IMigrator>().MigrateAsync(previousMigration);
        Assert.Equal(5, await database.AppUsers.CountAsync());
        await database.Database.MigrateAsync();
        Assert.Equal(new[] { "User" }, (await roles.GetAccessAsync(createdAfterMigration.Id, default))!.Roles);
    }

    internal static AppUser Account(string login, List<AppUserRole>? roles = null)
        => new()
        {
            Login = login,
            NormalizedLogin = login.ToUpperInvariant(),
            Roles = roles ?? [AppUserRole.User],
            PasswordHash = "",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed class RoleManagementTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RoleLifecycleEnforcesProtectionAndAuditsChanges()
    {
        await using var database = fixture.CreateContext();
        var admin = RoleAccessMigrationTests.Account("manager");
        var member = RoleAccessMigrationTests.Account("managed-member");
        database.AppUsers.AddRange(admin, member);
        await database.SaveChangesAsync();
        database.AccountRoles.Add(new AccountRole { UserId = admin.Id, RoleId = AccessRole.AdminId });
        await database.SaveChangesAsync();

        var roles = new RoleAccessService(database, TimeProvider.System);
        Assert.Equal(member.Id, Assert.Single(await roles.SearchAccountsAsync(admin.Id, "MANAGED", default)).Id);
        await Assert.ThrowsAsync<AppForbiddenException>(() => roles.GetRolesAsync(member.Id, default));
        await Assert.ThrowsAsync<Base409ConflictException>(() =>
            roles.UpdateRoleAsync(admin.Id, AccessRole.AdminId, new("Admin", [], "irrelevant"), default));
        await Assert.ThrowsAsync<Base409ConflictException>(() =>
            roles.DeleteRoleAsync(admin.Id, AccessRole.UserId, default));
        await Assert.ThrowsAsync<Base400BadRequestException>(() =>
            roles.CreateRoleAsync(admin.Id, new("No escrow", [AccessPermission.RolesManage]), default));
        await Assert.ThrowsAsync<Base400BadRequestException>(() =>
            roles.CreateRoleAsync(admin.Id, new("Unknown", ["unknown.permission"]), default));
        await Assert.ThrowsAsync<Base400BadRequestException>(() =>
            roles.SetAccountRolesAsync(admin.Id, member.Id, new([int.MaxValue]), default));

        var role = await roles.CreateRoleAsync(admin.Id, new("Reviewer", [AccessPermission.ImagesReviewQueue]),
            default);
        await Assert.ThrowsAsync<Base409ConflictException>(() =>
            roles.CreateRoleAsync(admin.Id, new("  reviewer  ", []), default));
        var account = await roles.SetAccountRolesAsync(admin.Id, member.Id, new([role.Id, role.Id]), default);
        Assert.Equal(new[] { AccessRole.UserId, role.Id }, account.RoleIds);
        Assert.Equal(new[] { AccessPermission.ImagesReviewQueue },
            (await roles.GetAccessAsync(member.Id, default))!.Permissions);

        await roles.UpdateRoleAsync(admin.Id, role.Id,
            new("Publisher", [AccessPermission.ImagesPublishAny], role.Version), default);
        Assert.Equal(new[] { AccessPermission.ImagesPublishAny },
            (await roles.GetAccessAsync(member.Id, default))!.Permissions);
        await Assert.ThrowsAsync<Base409ConflictException>(() => roles.DeleteRoleAsync(admin.Id, role.Id, default));

        await roles.SetAccountRolesAsync(admin.Id, member.Id, new([]), default);
        await roles.DeleteRoleAsync(admin.Id, role.Id, default);
        Assert.Empty((await roles.GetAccessAsync(member.Id, default))!.Permissions);
        var audit = await roles.GetAuditAsync(admin.Id, 1, 50, default);
        Assert.All(audit, entry => Assert.Equal(admin.Id, entry.ActorUserId));
        Assert.Equal(new[] { "deleteRole", "assignRoles", "updateRole", "assignRoles", "createRole" },
            audit.Select(entry => entry.Action));
    }
}

public sealed class RoleAdminConcurrencyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ConcurrentAdminRemovalsKeepAnAdministrator()
    {
        await using var database = fixture.CreateContext();
        var first = RoleAccessMigrationTests.Account("first-admin", [AppUserRole.Admin, AppUserRole.User]);
        var second = RoleAccessMigrationTests.Account("second-admin", [AppUserRole.Admin, AppUserRole.User]);
        database.AppUsers.AddRange(first, second);
        await database.SaveChangesAsync();
        database.AccountRoles.AddRange(
            new AccountRole { UserId = first.Id, RoleId = AccessRole.AdminId },
            new AccountRole { UserId = second.Id, RoleId = AccessRole.AdminId });
        await database.SaveChangesAsync();

        int[] baseline = [AccessRole.AdminId, AccessRole.UserId];
        var results = await Task.WhenAll(RemoveAdminAsync(first.Id, baseline), RemoveAdminAsync(second.Id, baseline));
        Assert.Single(results, result => result is null);
        Assert.IsType<Base409ConflictException>(Assert.Single(results, result => result is not null));
        var remainingAdminId = await database.AccountRoles.Where(membership => membership.RoleId == AccessRole.AdminId)
            .Select(membership => membership.UserId).SingleAsync();
        var removedAdminId = remainingAdminId == first.Id ? second.Id : first.Id;
        var audit = await database.AccessAudits.SingleAsync();
        Assert.Equal("assignRoles", audit.Action);
        Assert.Equal(removedAdminId, audit.TargetUserId);
        var roles = new RoleAccessService(database, TimeProvider.System);
        Assert.Equal(baseline, (await roles.GetAccountAsync(remainingAdminId, remainingAdminId, default)).RoleIds);
        Assert.Equal(new[] { AccessRole.UserId },
            (await roles.GetAccountAsync(remainingAdminId, removedAdminId, default)).RoleIds);
        Assert.Equal(new[] { AppUserRole.Admin, AppUserRole.User }, await database.AppUsers
            .Where(user => user.Id == remainingAdminId).Select(user => user.Roles).SingleAsync());
        Assert.Equal(new[] { AppUserRole.User }, await database.AppUsers
            .Where(user => user.Id == removedAdminId).Select(user => user.Roles).SingleAsync());
    }

    private async Task<Exception?> RemoveAdminAsync(int id, IReadOnlyList<int> baseline)
    {
        await using var database = fixture.CreateContext();
        var roles = new RoleAccessService(database, TimeProvider.System);
        return await Record.ExceptionAsync(() =>
            roles.SetAccountRolesAsync(id, id, new([]) { ExpectedRoleIds = baseline }, default));
    }
}

public sealed class RoleAssignmentConcurrencyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SameBaselineAllowsOneWriterAndStaleWriterCanRetryAfterReload()
    {
        await using var database = fixture.CreateContext();
        var first = RoleAccessMigrationTests.Account("assignment-admin-one", [AppUserRole.Admin, AppUserRole.User]);
        var second = RoleAccessMigrationTests.Account("assignment-admin-two", [AppUserRole.Admin, AppUserRole.User]);
        var target = RoleAccessMigrationTests.Account("assignment-target");
        database.AppUsers.AddRange(first, second, target);
        await database.SaveChangesAsync();
        database.AccountRoles.AddRange(
            new AccountRole { UserId = first.Id, RoleId = AccessRole.AdminId },
            new AccountRole { UserId = second.Id, RoleId = AccessRole.AdminId });
        await database.SaveChangesAsync();

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstService = new RoleAccessService(firstContext, TimeProvider.System);
        var secondService = new RoleAccessService(secondContext, TimeProvider.System);
        var firstBaseline = await firstService.GetAccountAsync(first.Id, target.Id, default);
        var secondBaseline = await secondService.GetAccountAsync(second.Id, target.Id, default);
        Assert.Equal(new[] { AccessRole.UserId }, firstBaseline.RoleIds);
        Assert.Equal(firstBaseline.RoleIds, secondBaseline.RoleIds);

        int[] firstDesired = [AccessRole.ModeratorId, AccessRole.UserId];
        int[] secondDesired = [AccessRole.AdminId, AccessRole.UserId, AccessRole.TrustedContributorId];
        var outcomes = await Task.WhenAll(
            Record.ExceptionAsync(() => firstService.SetAccountRolesAsync(first.Id, target.Id,
                new ChangeAccountRolesDto(firstDesired) { ExpectedRoleIds = firstBaseline.RoleIds }, default)),
            Record.ExceptionAsync(() => secondService.SetAccountRolesAsync(second.Id, target.Id,
                new ChangeAccountRolesDto(secondDesired) { ExpectedRoleIds = secondBaseline.RoleIds }, default)));
        Assert.Single(outcomes, error => error is null);
        Assert.IsType<RoleAssignmentsChangedException>(Assert.Single(outcomes, error => error is not null));

        var firstWon = outcomes[0] is null;
        var expectedWinner = firstWon ? firstDesired : secondDesired;
        var expectedWinnerLegacy = firstWon
            ? new[] { AppUserRole.Moderator, AppUserRole.User }
            : new[] { AppUserRole.Admin, AppUserRole.User };
        var audit = await database.AccessAudits.AsNoTracking().SingleAsync();
        Assert.Equal(firstWon ? first.Id : second.Id, audit.ActorUserId);
        Assert.Equal(target.Id, audit.TargetUserId);
        Assert.Equal("assignRoles", audit.Action);
        Assert.Equal(firstBaseline.RoleIds, JsonSerializer.Deserialize<int[]>(audit.Before!));
        Assert.Equal(expectedWinner, JsonSerializer.Deserialize<int[]>(audit.After!));
        Assert.Equal(expectedWinnerLegacy, await database.AppUsers.Where(user => user.Id == target.Id)
            .Select(user => user.Roles).SingleAsync());

        await using var retryContext = fixture.CreateContext();
        var retryService = new RoleAccessService(retryContext, TimeProvider.System);
        var losingActorId = firstWon ? second.Id : first.Id;
        var reloaded = await retryService.GetAccountAsync(losingActorId, target.Id, default);
        Assert.Equal(expectedWinner, reloaded.RoleIds);
        var desired = firstWon ? secondDesired : firstDesired;
        var retry = await retryService.SetAccountRolesAsync(losingActorId, target.Id,
            new ChangeAccountRolesDto(desired) { ExpectedRoleIds = reloaded.RoleIds }, default);
        Assert.Equal(desired, retry.RoleIds);
        var expectedRetryLegacy = firstWon
            ? new[] { AppUserRole.Admin, AppUserRole.User }
            : new[] { AppUserRole.Moderator, AppUserRole.User };
        Assert.Equal(expectedRetryLegacy, await database.AppUsers.Where(user => user.Id == target.Id)
            .Select(user => user.Roles).SingleAsync());
        var auditEntries = await database.AccessAudits.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync();
        Assert.Equal(2, auditEntries.Count);
        Assert.Equal(losingActorId, auditEntries[1].ActorUserId);
        Assert.Equal(target.Id, auditEntries[1].TargetUserId);
        Assert.Equal(expectedWinner, JsonSerializer.Deserialize<int[]>(auditEntries[1].Before!));
        Assert.Equal(desired, JsonSerializer.Deserialize<int[]>(auditEntries[1].After!));
    }
}

public sealed class RoleBootstrapTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ExistingBootstrapLoginIsNeverPromoted()
    {
        await using var database = fixture.CreateContext();
        var collision = RoleAccessMigrationTests.Account("bootstrap-collision");
        var admin = RoleAccessMigrationTests.Account("alternate-admin");
        database.AppUsers.AddRange(collision, admin);
        await database.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(database);
        services.AddSingleton(Options.Create(new DatabaseMigrationOptions()));
        services.AddSingleton(Options.Create(new BootstrapAdminOptions
        {
            Login = "bootstrap-collision",
            Password = "test-bootstrap-password"
        }));
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        await using var provider = services.BuildServiceProvider();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrationService.MigrateAsync(provider, NullLogger.Instance, default));
        Assert.Contains("existing account", failure.Message);

        database.AccountRoles.Add(new AccountRole { UserId = admin.Id, RoleId = AccessRole.AdminId });
        await database.SaveChangesAsync();
        await DatabaseMigrationService.MigrateAsync(provider, NullLogger.Instance, default);

        var access = new RoleAccessService(database, TimeProvider.System);
        Assert.Equal(new[] { "User" }, (await access.GetAccessAsync(collision.Id, default))!.Roles);
        Assert.Equal(new[] { "Admin", "User" }, (await access.GetAccessAsync(admin.Id, default))!.Roles);
        Assert.Empty(await database.AccessAudits.ToListAsync());
    }
}