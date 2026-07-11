using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Repository;

namespace GallerySiteBackend.ContextFactory;

public class RepositoryContextFactory : IDesignTimeDbContextFactory<RepositoryContext>
{
    public RepositoryContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration;
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "secrets.json")))
            configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("secrets.json").Build();
        else
            configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json").Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=gallery;Username=gallery;Password=gallery";
        var builder =
            new DbContextOptionsBuilder<RepositoryContext>().UseNpgsql(
                connectionString,
                b =>
                {
                b.UseVector();
                });


        return new RepositoryContext(builder.Options);
    }
}
