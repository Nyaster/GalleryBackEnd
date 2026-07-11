using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class ImageTagEfConfiguration : IEntityTypeConfiguration<ImageTag>
{
    public void Configure(EntityTypeBuilder<ImageTag> builder)
    {
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Name).HasMaxLength(64).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(64).IsRequired();
        builder.HasIndex(tag => tag.NormalizedName).IsUnique().HasDatabaseName("UX_ImageTags_NormalizedName");
    }
}
