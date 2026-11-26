using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

internal static class ImageViewModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImageViewCounter>(builder =>
        {
            builder.ToTable("ImageViewCounters");
            builder.HasKey(counter => counter.ImageId);
            builder.HasOne<AppImage>().WithOne().HasForeignKey<ImageViewCounter>(counter => counter.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageViewFirstSeen>(builder =>
        {
            builder.ToTable("ImageViewFirstSeen");
            builder.HasKey(view => new { view.ImageId, view.UserId });
            builder.HasIndex(view => new { view.ImageId, view.SeenAtUtc });
            builder.HasOne<AppImage>().WithMany().HasForeignKey(view => view.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
            // User IDs preserve aggregate history after an account is deleted.
        });

        modelBuilder.Entity<ImageViewBatchReceipt>(builder =>
        {
            builder.ToTable("ImageViewBatchReceipts");
            builder.HasKey(receipt => receipt.Id);
            builder.HasIndex(receipt => receipt.CreatedAtUtc);
        });
    }

    private sealed class ImageViewCounter
    {
        public int ImageId { get; set; }
        public long TotalViews { get; set; }
        public long UniqueRegisteredViewers { get; set; }
    }

    private sealed class ImageViewFirstSeen
    {
        public int ImageId { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset SeenAtUtc { get; set; }
    }

    private sealed class ImageViewBatchReceipt
    {
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
