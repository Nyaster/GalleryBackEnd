using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class AppImageEfConfiguration : IEntityTypeConfiguration<AppImage>
{
    public void Configure(EntityTypeBuilder<AppImage> builder)
    {
        builder.HasKey(image => image.Id);
        builder.HasDiscriminator<string>("image_type")
            .HasValue<UserMadeImage>("user")
            .HasValue<SelebusImage>("scraped");
        builder.Property(image => image.Source).HasConversion<string>().IsRequired();
        builder.Property(image => image.Visibility).HasConversion<string>().IsRequired();
        builder.Property(image => image.ModerationStatus).HasConversion<string>().IsRequired();
        builder.Property(image => image.AiUsage).HasConversion<string>().IsRequired();
        builder.Property(image => image.EmbeddingStatus).HasConversion<string>().IsRequired();
        builder.Property(image => image.StorageKey).HasMaxLength(260).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(image => image.Embedding).HasColumnType("vector(1024)");
        builder.HasOne(image => image.UploadedBy)
            .WithMany(user => user.UploadedImages)
            .HasForeignKey(image => image.UploadedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(image => image.Tags).WithMany(tag => tag.AppImages);
        builder.HasIndex(image => new { image.Source, image.ExternalMediaId })
            .IsUnique()
            .HasFilter("\"ExternalMediaId\" IS NOT NULL");
        builder.HasIndex(image => new { image.ModerationStatus, image.Visibility, image.UploadedAtUtc });
        builder.HasIndex(image => new { image.AiUsage, image.ModerationStatus, image.Visibility, image.UploadedAtUtc });
        builder.HasIndex(image => image.DeletedAtUtc);
        builder.HasIndex(image => image.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_l2_ops")
            .HasFilter("\"Embedding\" IS NOT NULL");
    }
}
