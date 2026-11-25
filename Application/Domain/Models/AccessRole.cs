namespace Entities.Models;

public sealed class AccessRole
{
    public const int AdminId = 1;
    public const int ModeratorId = 2;
    public const int UserId = 3;
    public const int TrustedContributorId = 4;

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public bool IsProtected { get; set; }
    public List<AccessRolePermission> Permissions { get; set; } = [];
}

public sealed class AccessRolePermission
{
    public int RoleId { get; set; }
    public required string PermissionKey { get; set; }
}

public sealed class AccountRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public AccessRole Role { get; set; } = null!;
}

public sealed class AccessAudit
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public required string Action { get; set; }
    public int? TargetUserId { get; set; }
    public int? RoleId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
