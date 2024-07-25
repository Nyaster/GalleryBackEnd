using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GallerySiteBackend.Models;

public class AppUserEfConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Login).IsRequired();
        builder.Property(x => x.Password).IsRequired();
        builder.Property(x => x.Salt).IsRequired();
        builder.Property(x => x.RefreshToken).IsRequired(false);
        builder.Property(x => x.RefreshTokenExp).IsRequired(false);
        builder.HasMany(x => x.UploadedImages).WithOne(x => x.UploadedBy).HasForeignKey(x => x.UploadedById);
    }
}