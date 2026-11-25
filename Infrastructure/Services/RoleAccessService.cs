using System.Security.Cryptography;
using System.Text.Json;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Repository;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public sealed class RoleAccessService(RepositoryContext database, TimeProvider clock)
    : IRoleAccessService, IRoleManagementService
{
    public async Task<AccountAccessDto?> GetAccessAsync(int userId, CancellationToken cancellationToken)
    {
        var roles = await database.AppUsers.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => database.AccountRoles.Where(membership => membership.UserId == user.Id)
                .OrderBy(membership => membership.RoleId)
                .Select(membership => new
                {
                    membership.Role.Name,
                    Permissions = membership.Role.Permissions.Select(permission => permission.PermissionKey).ToList()
                })
                .ToList())
            .SingleOrDefaultAsync(cancellationToken);

        return roles is null
            ? null
            : new AccountAccessDto(roles.Select(role => role.Name).ToArray(),
                roles.SelectMany(role => role.Permissions).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<AccessPermissionDto>> GetPermissionsAsync(int actorId,
        CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        return AccessPermission.Catalog;
    }

    public async Task<IReadOnlyList<AccessRoleDto>> GetRolesAsync(int actorId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        var roles = await database.AccessRoles.AsNoTracking().OrderBy(role => role.Id)
            .Select(role => new
            {
                role.Id,
                role.Name,
                role.IsProtected,
                Permissions = role.Permissions.Select(permission => permission.PermissionKey).ToList(),
                MemberCount = database.AccountRoles.Count(membership => membership.RoleId == role.Id)
            }).ToListAsync(cancellationToken);
        return roles.Select(role => ToDto(role.Id, role.Name, role.IsProtected, role.Permissions, role.MemberCount))
            .ToArray();
    }

    public async Task<IReadOnlyList<AccessAccountDto>> GetMembersAsync(int actorId, int roleId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        var offset = PageOffset(page, pageSize);
        if (!await database.AccessRoles.AnyAsync(role => role.Id == roleId, cancellationToken))
            throw new Base404ReturnException("Role not found.");
        var accounts = database.AppUsers.AsNoTracking().Where(user => database.AccountRoles.Any(membership =>
            membership.RoleId == roleId && membership.UserId == user.Id)).OrderBy(user => user.NormalizedLogin);
        return await ProjectAccounts(accounts.Skip(offset).Take(pageSize)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessAccountDto>> SearchAccountsAsync(int actorId, string? search,
        CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        var normalized = search?.Trim().ToUpperInvariant();
        var accounts = database.AppUsers.AsNoTracking();
        if (!string.IsNullOrEmpty(normalized))
            accounts = accounts.Where(user => user.NormalizedLogin.Contains(normalized));
        return await ProjectAccounts(accounts.OrderBy(user => user.NormalizedLogin).Take(100))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccessAccountDto> GetAccountAsync(int actorId, int id, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        return await ProjectAccounts(database.AppUsers.AsNoTracking().Where(user => user.Id == id))
                   .SingleOrDefaultAsync(cancellationToken)
               ?? throw new Base404ReturnException("Account not found.");
    }

    public async Task<AccountAccessDto> GetAccountPermissionsAsync(int actorId, int id,
        CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        return await GetAccessAsync(id, cancellationToken) ?? throw new Base404ReturnException("Account not found.");
    }

    public async Task<AccessAccountDto> SetAccountRolesAsync(int actorId, int id, ChangeAccountRolesDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AcquireWriteLockAsync(database, cancellationToken);
        await RequireManagerAsync(actorId, cancellationToken);
        var account = await database.AppUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken)
                      ?? throw new Base404ReturnException("Account not found.");
        var memberships = await database.AccountRoles.Where(membership => membership.UserId == id)
            .ToListAsync(cancellationToken);
        int[] before = memberships.Select(membership => membership.RoleId).Order().ToArray();
        if (request.ExpectedRoleIds is { } expected && !expected.ToHashSet().SetEquals(before))
            throw new RoleAssignmentsChangedException();
        int[] after = request.RoleIds.Append(AccessRole.UserId).Distinct().Order().ToArray();
        if (await database.AccessRoles.CountAsync(role => after.Contains(role.Id), cancellationToken) != after.Length)
            throw new Base400BadRequestException("One or more role IDs are unknown.");
        if (before.Contains(AccessRole.AdminId) && !after.Contains(AccessRole.AdminId) &&
            !await database.AccountRoles.AnyAsync(membership =>
                membership.RoleId == AccessRole.AdminId && membership.UserId != id, cancellationToken))
            throw new Base409ConflictException("At least one account must retain the Admin role.");

        if (!before.SequenceEqual(after))
        {
            database.AccountRoles.RemoveRange(memberships.Where(membership => !after.Contains(membership.RoleId)));
            foreach (var roleId in after.Except(before))
                database.AccountRoles.Add(new AccountRole { UserId = id, RoleId = roleId });
            // Preserve the current built-in grants if this migration is rolled back.
            account.Roles = after.Where(roleId => roleId <= AccessRole.UserId)
                .Select(roleId => (AppUserRole)(roleId - 1)).ToList();
            AddAudit(actorId, "assignRoles", id, null, before, after);
            await database.SaveChangesAsync(cancellationToken);
        }

        var result = await ProjectAccounts(database.AppUsers.Where(user => user.Id == id))
            .SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AccessRoleDto> CreateRoleAsync(int actorId, ChangeAccessRoleDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AcquireWriteLockAsync(database, cancellationToken);
        await RequireManagerAsync(actorId, cancellationToken);
        var (name, normalized, keys) =
            await ValidateRoleAsync(request.Name, request.Permissions, null, cancellationToken);
        var role = new AccessRole
        {
            Name = name,
            NormalizedName = normalized,
            Permissions = keys.Select(key => new AccessRolePermission { PermissionKey = key }).ToList()
        };
        database.AccessRoles.Add(role);
        await database.SaveChangesAsync(cancellationToken);
        var result = ToDto(role);
        AddAudit(actorId, "createRole", null, role.Id, null, result);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<AccessRoleDto> UpdateRoleAsync(int actorId, int id, UpdateAccessRoleDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await AcquireWriteLockAsync(database, cancellationToken);
        await RequireManagerAsync(actorId, cancellationToken);
        var role = await GetEditableRoleAsync(id, cancellationToken);
        var before = ToDto(role);
        if (!string.Equals(before.Version, request.ExpectedVersion, StringComparison.Ordinal))
            throw new RoleDefinitionChangedException();
        var (name, normalized, keys) =
            await ValidateRoleAsync(request.Name, request.Permissions, id, cancellationToken);
        var count = await database.AccountRoles.CountAsync(membership => membership.RoleId == id, cancellationToken);
        before = before with { MemberCount = count };
        var result = before;

        if (before.Name != name || !before.Permissions.SequenceEqual(keys))
        {
            role.Name = name;
            role.NormalizedName = normalized;
            role.Permissions.RemoveAll(permission => !keys.Contains(permission.PermissionKey, StringComparer.Ordinal));
            foreach (var key in keys.Except(before.Permissions, StringComparer.Ordinal))
                role.Permissions.Add(new AccessRolePermission { PermissionKey = key });
            result = ToDto(role) with { MemberCount = count };
            AddAudit(actorId, "updateRole", null, id, before, result);
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task DeleteRoleAsync(int actorId, int id, CancellationToken cancellationToken)
    {
        await using var transaction = await AcquireWriteLockAsync(database, cancellationToken);
        await RequireManagerAsync(actorId, cancellationToken);
        var role = await GetEditableRoleAsync(id, cancellationToken);
        if (await database.AccountRoles.AnyAsync(membership => membership.RoleId == id, cancellationToken))
            throw new Base409ConflictException("Remove all assignments before deleting this role.");
        database.AccessRoles.Remove(role);
        AddAudit(actorId, "deleteRole", null, id, ToDto(role), null);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessAuditDto>> GetAuditAsync(int actorId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await RequireManagerAsync(actorId, cancellationToken);
        var offset = PageOffset(page, pageSize);
        return await database.AccessAudits.AsNoTracking()
            .OrderByDescending(entry => entry.Id).Skip(offset).Take(pageSize)
            .Select(entry => new AccessAuditDto(entry.Id, entry.ActorUserId, entry.Action, entry.TargetUserId,
                entry.RoleId, entry.Before, entry.After, entry.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public static async Task<IDbContextTransaction> AcquireWriteLockAsync(RepositoryContext database,
        CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize admin mutations and bootstrap even when there are no role memberships yet.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1748229461, 1)",
                cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task RequireManagerAsync(int actorId, CancellationToken cancellationToken)
    {
        if (!await database.AccountRoles.AnyAsync(membership => membership.UserId == actorId
                                                                && membership.RoleId == AccessRole.AdminId &&
                                                                membership.Role.Permissions.Any(permission =>
                                                                    permission.PermissionKey ==
                                                                    AccessPermission.RolesManage), cancellationToken))
            throw new AppForbiddenException("You do not have permission to manage roles.");
    }

    private IQueryable<AccessAccountDto> ProjectAccounts(IQueryable<AppUser> accounts)
    {
        var now = clock.GetUtcNow();
        return accounts.Select(user => new AccessAccountDto(user.Id, user.Login,
            database.AccountRoles.Where(membership => membership.UserId == user.Id)
                .OrderBy(membership => membership.RoleId).Select(membership => membership.RoleId).ToList(),
            database.AccountRoles.Any(membership =>
                membership.UserId == user.Id && membership.RoleId == AccessRole.AdminId)
            || (!user.UploadsBlocked && (user.CanUploadImages || user.AuthenticatorEnabledAtUtc != null)),
            user.UploadsBlocked, user.CanUploadImages, user.AuthenticatorEnabledAtUtc != null,
            user.CommentRestriction != null && (user.CommentRestriction.RestrictedUntilUtc == null ||
                                                user.CommentRestriction.RestrictedUntilUtc > now)));
    }

    private static int PageOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            throw new Base400BadRequestException("Invalid page or page size.");
        return (page - 1) * pageSize;
    }

    private async Task<AccessRole> GetEditableRoleAsync(int id, CancellationToken cancellationToken)
    {
        var role = await database.AccessRoles.Include(role => role.Permissions)
                       .SingleOrDefaultAsync(role => role.Id == id, cancellationToken)
                   ?? throw new Base404ReturnException("Role not found.");
        if (role.IsProtected)
            throw new Base409ConflictException("Protected roles cannot be changed or deleted.");
        return role;
    }

    private async Task<(string Name, string NormalizedName, string[] Keys)> ValidateRoleAsync(
        string requestedName, IReadOnlyList<string> permissions, int? id, CancellationToken cancellationToken)
    {
        var name = requestedName.Trim();
        var normalized = name.ToUpperInvariant();
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl))
            throw new Base400BadRequestException("Role name must contain between 1 and 64 printable characters.");

        if (normalized is "ADMIN" or "USER" ||
            (normalized == "MODERATOR" && id != AccessRole.ModeratorId) ||
            (normalized == "TRUSTED CONTRIBUTOR" && id != AccessRole.TrustedContributorId) ||
            await database.AccessRoles.AnyAsync(role => role.NormalizedName == normalized && role.Id != id,
                cancellationToken))
            throw new Base409ConflictException("This role name is already in use or reserved.");

        var keys = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (keys.Except(AccessPermission.AllKeys, StringComparer.Ordinal).Any())
            throw new Base400BadRequestException("One or more permission keys are unknown.");
        if (keys.Any(key => key is AccessPermission.RolesManage or AccessPermission.UploadsBypassRestrictions))
            throw new Base400BadRequestException("Admin-only permissions cannot be added to an editable role.");
        return (name, normalized, keys);
    }

    private static AccessRoleDto ToDto(AccessRole role)
        => ToDto(role.Id, role.Name, role.IsProtected, role.Permissions.Select(permission => permission.PermissionKey),
            0);

    private static AccessRoleDto ToDto(int id, string name, bool isProtected, IEnumerable<string> permissions,
        int memberCount)
    {
        var keys = permissions.Order(StringComparer.Ordinal).ToArray();
        var definition = JsonSerializer.SerializeToUtf8Bytes(new { id, name, permissions = keys });
        var version = Convert.ToHexString(SHA256.HashData(definition));
        return new AccessRoleDto(id, name, isProtected, keys, version, memberCount);
    }

    private void AddAudit(int actorId, string action, int? targetUserId, int? roleId, object? before, object? after)
    {
        database.AccessAudits.Add(new AccessAudit
        {
            ActorUserId = actorId,
            Action = action,
            TargetUserId = targetUserId,
            RoleId = roleId,
            Before = before is null ? null : JsonSerializer.Serialize(before, JsonSerializerOptions.Web),
            After = after is null ? null : JsonSerializer.Serialize(after, JsonSerializerOptions.Web),
            CreatedAtUtc = clock.GetUtcNow()
        });
    }
}