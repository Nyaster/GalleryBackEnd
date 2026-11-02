using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class AppUserEfConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Login).HasMaxLength(64).IsRequired();
        builder.Property(user => user.NormalizedLogin).HasMaxLength(64).IsRequired();
        builder.HasIndex(user => user.NormalizedLogin).IsUnique().HasDatabaseName("UX_AppUsers_NormalizedLogin");
        builder.Property(user => user.PasswordHash).IsRequired();
        builder.PrimitiveCollection(user => user.Roles).HasColumnName("roles");
        builder.Property(user => user.CanUploadImages).HasDefaultValue(false).IsRequired();
        builder.Property(user => user.UploadsBlocked).HasDefaultValue(false);
        builder.Property(user => user.AuthenticationVersion).HasDefaultValue(0);
        builder.Property(user => user.AuthenticatorFailedAttempts).HasDefaultValue(0);
        builder.PrimitiveCollection(user => user.AuthenticatorBackupCodeHashes)
            .HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        builder.HasMany(user => user.RefreshSessions).WithOne(session => session.User)
            .HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}