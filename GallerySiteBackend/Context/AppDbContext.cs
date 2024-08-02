using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace GallerySiteBackend.Context;

public class AppDbContext : DbContext{
  public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppUserEfConfiguration).Assembly);
    }
    public DbSet<ImageTag> Tags { get; init; }
    public DbSet<AppImage> Images { get; init; }
    public DbSet<AppUser> AppUsers { get; init; }
}