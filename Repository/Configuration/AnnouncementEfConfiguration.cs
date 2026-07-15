using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class AnnouncementEfConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.HasKey(announcement => announcement.Id);
        builder.Property(announcement => announcement.Id).ValueGeneratedNever();
        builder.Property(announcement => announcement.Content).HasMaxLength(2000).IsRequired();
        builder.HasOne(announcement => announcement.UpdatedByUser).WithMany().HasForeignKey(announcement => announcement.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FeedbackEfConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.HasKey(feedback => feedback.Id);
        builder.Property(feedback => feedback.Content).HasMaxLength(2000).IsRequired();
        builder.HasOne(feedback => feedback.Author).WithMany().HasForeignKey(feedback => feedback.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(feedback => new { feedback.CreatedAtUtc, feedback.Id });
        builder.HasIndex(feedback => feedback.AuthorId);
    }
}
