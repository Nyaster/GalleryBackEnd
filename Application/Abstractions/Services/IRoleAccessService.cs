using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IRoleAccessService
{
    Task<AccountAccessDto?> GetAccessAsync(int userId, CancellationToken cancellationToken);
}

public interface IRoleManagementService
{
    Task<IReadOnlyList<AccessPermissionDto>> GetPermissionsAsync(int actorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessRoleDto>> GetRolesAsync(int actorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessAccountDto>> GetMembersAsync(int actorId, int roleId, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessAccountDto>> SearchAccountsAsync(int actorId, string? search,
        CancellationToken cancellationToken);

    Task<AccessAccountDto> GetAccountAsync(int actorId, int id, CancellationToken cancellationToken);
    Task<AccountAccessDto> GetAccountPermissionsAsync(int actorId, int id, CancellationToken cancellationToken);

    Task<AccessAccountDto> SetAccountRolesAsync(int actorId, int id, ChangeAccountRolesDto request,
        CancellationToken cancellationToken);

    Task<AccessRoleDto> CreateRoleAsync(int actorId, ChangeAccessRoleDto request, CancellationToken cancellationToken);

    Task<AccessRoleDto> UpdateRoleAsync(int actorId, int id, UpdateAccessRoleDto request,
        CancellationToken cancellationToken);

    Task DeleteRoleAsync(int actorId, int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessAuditDto>> GetAuditAsync(int actorId, int page, int pageSize,
        CancellationToken cancellationToken);
}