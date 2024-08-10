using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Repository;

namespace GallerySiteBackend.ContextFactory;

public class RepositoryContextFactory : IDesignTimeDbContextFactory<RepositoryContext>
{
    public RepositoryContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration;
        if (File.Exists(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "/secrets.json"))
        {
            configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("secrets.json").Build();
        }
        else
        {
            configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json").Build();
        }

        var builder =
            new DbContextOptionsBuilder<RepositoryContext>().UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),b=>b.MigrationsAssembly("GallerySiteBackend"));


        return new RepositoryContext(builder.Options);
    }
}