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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppImageEfConfiguration).Assembly);
    }
}