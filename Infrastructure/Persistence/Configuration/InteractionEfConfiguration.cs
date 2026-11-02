using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class CommentEfConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(comment => comment.Id);
        builder.Property(comment => comment.Content).HasMaxLength(2000).IsRequired();
        builder.HasOne(comment => comment.Image).WithMany(image => image.Comments).HasForeignKey(comment => comment.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(comment => comment.Author).WithMany(user => user.Comments).HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(comment => new { comment.ImageId, comment.DeletedAtUtc, comment.CreatedAtUtc, comment.Id });
        builder.HasIndex(comment => new { comment.CreatedAtUtc, comment.ImageId })
            .HasFilter("\"DeletedAtUtc\" IS NULL");
    }
}

public sealed class CommentRestrictionEfConfiguration : IEntityTypeConfiguration<CommentRestriction>
{
    public void Configure(EntityTypeBuilder<CommentRestriction> builder)
    {
        builder.HasKey(restriction => restriction.UserId);
        builder.HasOne(restriction => restriction.User).WithOne(user => user.CommentRestriction).HasForeignKey<CommentRestriction>(restriction => restriction.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImageLikeEfConfiguration : IEntityTypeConfiguration<ImageLike>
{
    public void Configure(EntityTypeBuilder<ImageLike> builder)
    {
        builder.HasKey(like => new { like.ImageId, like.UserId });
        builder.HasOne(like => like.Image).WithMany(image => image.Likes).HasForeignKey(like => like.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(like => like.User).WithMany(user => user.ImageLikes).HasForeignKey(like => like.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(like => new { like.UserId, like.CreatedAtUtc, like.ImageId });
        builder.HasIndex(like => new { like.CreatedAtUtc, like.ImageId });
    }
}

public sealed class ImageLikeActivityEfConfiguration : IEntityTypeConfiguration<ImageLikeActivity>
{
    public void Configure(EntityTypeBuilder<ImageLikeActivity> builder)
    {
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Type).HasConversion<string>().IsRequired();
        builder.HasIndex(activity => new { activity.OccurredAtUtc, activity.ImageId });
    }
}

public sealed class RankingSnapshotEfConfiguration : IEntityTypeConfiguration<RankingSnapshot>
{
    public void Configure(EntityTypeBuilder<RankingSnapshot> builder)
    {
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Period).HasConversion<string>().IsRequired();
        builder.HasIndex(snapshot => new { snapshot.Period, snapshot.PeriodStartUtc }).IsUnique();
        builder.HasMany(snapshot => snapshot.Entries).WithOne(entry => entry.Snapshot).HasForeignKey(entry => entry.SnapshotId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RankingSnapshotEntryEfConfiguration : IEntityTypeConfiguration<RankingSnapshotEntry>
{
    public void Configure(EntityTypeBuilder<RankingSnapshotEntry> builder)
    {
        builder.HasKey(entry => new { entry.SnapshotId, entry.ImageId });
        builder.HasIndex(entry => new { entry.SnapshotId, entry.Rank }).IsUnique();
    }
}
