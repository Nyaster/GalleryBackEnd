using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Repository.Configuration;

namespace Repository;

public sealed class RepositoryContext(DbContextOptions<RepositoryContext> options) : DbContext(options)
{
    public DbSet<ImageTag> Tags => Set<ImageTag>();
    public DbSet<AppImage> Images => Set<AppImage>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();
    public DbSet<SelebusImage> SelebusImages => Set<SelebusImage>();
    public DbSet<UserMadeImage> UserMadeImages => Set<UserMadeImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppImageEfConfiguration).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
