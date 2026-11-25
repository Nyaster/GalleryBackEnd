using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DataTransferObjects;

public sealed record AccountAccessDto(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record AccessPermissionDto(string Key, string Description);

public sealed record AccessRoleDto(
    int Id,
    string Name,
    bool IsProtected,
    IReadOnlyList<string> Permissions,
    string Version,
    int MemberCount = 0);

public sealed record AccessAccountDto(
    int Id,
    string Login,
    IReadOnlyList<int> RoleIds,
    bool CanUploadImages,
    bool UploadsBlocked,
    bool UploadPermissionGranted,
    bool AuthenticatorEnabled,
    bool CommentsRestricted);

public sealed record ChangeAccessRoleDto([Required] string Name, [Required] IReadOnlyList<string> Permissions);

public sealed record UpdateAccessRoleDto(
    [Required] string Name,
    [Required] IReadOnlyList<string> Permissions,
    [Required] string ExpectedVersion);

public sealed record ChangeAccountRolesDto([Required] IReadOnlyList<int> RoleIds)
{
    // Omission is supported during rollout. A supplied baseline must be an array, including [].
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? ExpectedRoleIds
    {
        get;
        init => field = value ?? throw new JsonException("expectedRoleIds must be an array.");
    }
}

public sealed record AccessAuditDto(
    long Id,
    int? ActorUserId,
    string Action,
    int? TargetUserId,
    int? RoleId,
    string? Before,
    string? After,
    DateTimeOffset CreatedAtUtc);