﻿using System.ComponentModel.DataAnnotations.Schema;
using System.Numerics;
using GallerySiteBackend.Models;

namespace Entities.Models;

public abstract class AppImage
{
    public int Id { get; set; }
    public int MediaId { get; set; }
    public AppUser UploadedBy { get; set; }
    public int UploadedById { get; set; }
    public DateTime UploadedDate { get; set; }
    public int LikesCount { get; set; }
    public int FavoritesCount { get; set; }
    public List<AppUser> LikedBy { get; set; }
    public List<AppUser> FavoriteBy { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public List<ImageTag> Tags { get; set; }
    public required string PathToFileOnDisc { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Pgvector.Vector Embedding { get; set; }
}