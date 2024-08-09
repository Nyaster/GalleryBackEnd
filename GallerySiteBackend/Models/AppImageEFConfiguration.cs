﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GallerySiteBackend.Models;

public class AppImageEfConfiguration : IEntityTypeConfiguration<AppImage>
{
    public void Configure(EntityTypeBuilder<AppImage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UploadedDate).IsRequired(true);
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.IsHidden).IsRequired();
        builder.Property(x => x.LikesCount).IsRequired();
        builder.Property(x => x.FavoritesCount).IsRequired();
        builder.HasMany(x => x.LikedBy).WithMany();
        builder.HasMany(x => x.FavoriteBy).WithMany();
        builder.HasMany(x => x.Tags).WithMany(x => x.AppImages);
        builder.Property(x => x.PathToFileOnDisc).IsRequired();
    }
}