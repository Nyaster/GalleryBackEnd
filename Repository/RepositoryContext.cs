using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;
using Repository.Configuration;

namespace Repository;

public class RepositoryContext : DbContext
{
    protected RepositoryContext()
    {
    }

    public RepositoryContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<ImageTag> Tags { get; init; }
    public DbSet<AppImage> Images { get; init; }
    public DbSet<AppUser> AppUsers { get; init; }
    public DbSet<SelebusImage> SelebusImages { get; init; }
    public DbSet<UserMadeImage> UserMadeImages { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppImageEfConfiguration).Assembly);
    
    }
}