using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GallerySiteBackend.Models;

public class ImageTagEfConfiguration : IEntityTypeConfiguration<ImageTag>
{
    public void Configure(EntityTypeBuilder<ImageTag> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IsDeleted);
        builder.Property(x => x.CreatDateTime);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
    }
}