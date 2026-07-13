using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class ScrapeRunEfConfiguration : IEntityTypeConfiguration<ScrapeRun>
{
    public void Configure(EntityTypeBuilder<ScrapeRun> builder)
    {
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Mode).HasConversion<string>();
        builder.Property(run => run.Status).HasConversion<string>();
        builder.Property(run => run.Error).HasMaxLength(2048);
        builder.HasIndex(run => new { run.Status, run.CreatedAtUtc });
        builder.HasIndex(run => run.Status).HasFilter("\"Status\" IN ('Queued', 'Running')").IsUnique();
    }
}
