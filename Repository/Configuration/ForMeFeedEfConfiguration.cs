using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class ForMeFeedEfConfiguration : IEntityTypeConfiguration<ForMeFeed>
{
    public void Configure(EntityTypeBuilder<ForMeFeed> builder)
    {
        builder.HasKey(feed => feed.Id);
        builder.Property(feed => feed.Mode).HasMaxLength(16).IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(feed => feed.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(feed => feed.Entries).WithOne().HasForeignKey(entry => entry.FeedId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(feed => new { feed.UserId, feed.ExpiresAtUtc });
        builder.HasIndex(feed => feed.ExpiresAtUtc);
    }
}

public sealed class ForMeFeedEntryEfConfiguration : IEntityTypeConfiguration<ForMeFeedEntry>
{
    public void Configure(EntityTypeBuilder<ForMeFeedEntry> builder)
    {
        builder.HasKey(entry => new { entry.FeedId, entry.Rank });
        builder.HasIndex(entry => new { entry.FeedId, entry.ImageId }).IsUnique();
        builder.HasOne<AppImage>().WithMany().HasForeignKey(entry => entry.ImageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
