using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class AccessRoleEfConfiguration : IEntityTypeConfiguration<AccessRole>
{
    public void Configure(EntityTypeBuilder<AccessRole> builder)
    {
        builder.Property(role => role.Name).HasMaxLength(64);
        builder.Property(role => role.NormalizedName).HasMaxLength(64);
        builder.HasIndex(role => role.NormalizedName).IsUnique();
        builder.HasMany(role => role.Permissions).WithOne().HasForeignKey(permission => permission.RoleId);
    }
}

public sealed class AccessRolePermissionEfConfiguration : IEntityTypeConfiguration<AccessRolePermission>
{
    public void Configure(EntityTypeBuilder<AccessRolePermission> builder)
    {
        builder.HasKey(permission => new { permission.RoleId, permission.PermissionKey });
        builder.Property(permission => permission.PermissionKey).HasMaxLength(64);
    }
}

public sealed class AccountRoleEfConfiguration : IEntityTypeConfiguration<AccountRole>
{
    public void Configure(EntityTypeBuilder<AccountRole> builder)
    {
        builder.HasKey(membership => new { membership.UserId, membership.RoleId });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(membership => membership.UserId);
        builder.HasOne(membership => membership.Role).WithMany().HasForeignKey(membership => membership.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccessAuditEfConfiguration : IEntityTypeConfiguration<AccessAudit>
{
    public void Configure(EntityTypeBuilder<AccessAudit> builder)
    {
        builder.Property(entry => entry.Action).HasMaxLength(32);
    }
}
