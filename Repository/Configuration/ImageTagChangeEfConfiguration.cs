using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class ImageTagChangeEfConfiguration : IEntityTypeConfiguration<ImageTagChange>
{
    public void Configure(EntityTypeBuilder<ImageTagChange> builder)
    {
        builder.HasKey(change => change.Id);
        builder.PrimitiveCollection(change => change.PreviousTags).HasColumnName("PreviousTags");
        builder.PrimitiveCollection(change => change.ProposedTags).HasColumnName("ProposedTags");
        builder.Property(change => change.Kind).HasConversion<string>().IsRequired();
        builder.Property(change => change.Status).HasConversion<string>().IsRequired();
        builder.Property(change => change.EditedByLogin).HasMaxLength(64).IsRequired();
        builder.Property(change => change.ReviewedByLogin).HasMaxLength(64);
        builder.Property(change => change.RevertedByLogin).HasMaxLength(64);
        builder.Property(change => change.ReviewNote).HasMaxLength(500);
        builder.Property(change => change.ReversionNote).HasMaxLength(500);
        builder.HasOne(change => change.Image).WithMany(image => image.TagChanges)
            .HasForeignKey(change => change.ImageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.ImageId, change.CreatedAtUtc, change.Id });
        builder.HasIndex(change => new { change.Status, change.CreatedAtUtc, change.Id });
        builder.HasIndex(change => change.ImageId).IsUnique().HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("UX_ImageTagChanges_PendingImage");
    }
}
