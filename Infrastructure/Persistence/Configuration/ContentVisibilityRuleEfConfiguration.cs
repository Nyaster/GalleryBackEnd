using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Repository.Configuration;

public sealed class ContentVisibilityRuleEfConfiguration : IEntityTypeConfiguration<ContentVisibilityRule>
{
    internal const string GroupKeyIndex = "UX_ContentVisibilityRules_TagGroupKey";

    public void Configure(EntityTypeBuilder<ContentVisibilityRule> builder)
    {
        builder.Property(rule => rule.TagGroupKey).HasMaxLength(64);
        builder.HasIndex(rule => rule.TagGroupKey).IsUnique().HasDatabaseName(GroupKeyIndex);
        builder.HasMany(rule => rule.TagLinks).WithOne()
            .HasForeignKey(link => link.ContentVisibilityRuleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ContentVisibilityRuleTagEfConfiguration : IEntityTypeConfiguration<ContentVisibilityRuleTag>
{
    public void Configure(EntityTypeBuilder<ContentVisibilityRuleTag> builder)
    {
        builder.HasKey(link => new { link.ContentVisibilityRuleId, link.TagId });
        builder.HasOne<ImageTag>().WithMany().HasForeignKey(link => link.TagId).OnDelete(DeleteBehavior.Restrict);
    }
}