using Entities.Models;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteBackend.Presentation;

[ApiController]
[Authorize(Policy = AccessPermission.RolesManage)]
[Route("api/admin/access")]
public sealed class AccessController(
    IRoleManagementService roles,
    IUserContext currentUser,
    IOptions<RoleAssignmentOptions> assignmentOptions) : ControllerBase
{
    [HttpGet("accounts")]
    public Task<IReadOnlyList<AccessAccountDto>> Accounts([FromQuery] string? search,
        CancellationToken cancellationToken)
        => roles.SearchAccountsAsync(currentUser.UserId!.Value, search, cancellationToken);

    [HttpGet("accounts/{id:int}/roles")]
    public Task<AccessAccountDto> AccountRoles(int id, CancellationToken cancellationToken)
        => roles.GetAccountAsync(currentUser.UserId!.Value, id, cancellationToken);

    [HttpPut("accounts/{id:int}/roles")]
    public Task<AccessAccountDto> SetAccountRoles(int id, ChangeAccountRolesDto request,
        CancellationToken cancellationToken)
    {
        if (assignmentOptions.Value.RequireExpectedRoleIds && request.ExpectedRoleIds is null)
            throw new Entities.Exceptions.Base400BadRequestException("expectedRoleIds is required.");
        return roles.SetAccountRolesAsync(currentUser.UserId!.Value, id, request, cancellationToken);
    }

    [HttpGet("accounts/{id:int}/permissions")]
    public Task<AccountAccessDto> AccountPermissions(int id, CancellationToken cancellationToken)
        => roles.GetAccountPermissionsAsync(currentUser.UserId!.Value, id, cancellationToken);

    [HttpGet("roles")]
    public Task<IReadOnlyList<AccessRoleDto>> Roles(CancellationToken cancellationToken)
        => roles.GetRolesAsync(currentUser.UserId!.Value, cancellationToken);

    [HttpPost("roles")]
    public async Task<ActionResult<AccessRoleDto>> CreateRole(ChangeAccessRoleDto request,
        CancellationToken cancellationToken)
    {
        var role = await roles.CreateRoleAsync(currentUser.UserId!.Value, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, role);
    }

    [HttpGet("roles/{id:int}/members")]
    public Task<IReadOnlyList<AccessAccountDto>> Members(int id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => roles.GetMembersAsync(currentUser.UserId!.Value, id, page, pageSize, cancellationToken);

    [HttpPut("roles/{id:int}")]
    public Task<AccessRoleDto> UpdateRole(int id, UpdateAccessRoleDto request, CancellationToken cancellationToken)
        => roles.UpdateRoleAsync(currentUser.UserId!.Value, id, request, cancellationToken);

    [HttpDelete("roles/{id:int}")]
    public async Task<IActionResult> DeleteRole(int id, CancellationToken cancellationToken)
    {
        await roles.DeleteRoleAsync(currentUser.UserId!.Value, id, cancellationToken);
        return NoContent();
    }

    [HttpGet("permissions")]
    public Task<IReadOnlyList<AccessPermissionDto>> Permissions(CancellationToken cancellationToken)
        => roles.GetPermissionsAsync(currentUser.UserId!.Value, cancellationToken);

    [HttpGet("audit")]
    public Task<IReadOnlyList<AccessAuditDto>> Audit([FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => roles.GetAuditAsync(currentUser.UserId!.Value, page, pageSize, cancellationToken);
}