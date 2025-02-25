using Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Repository;

public static class RepositoryDiInjectionConfiguration
{
    public static void ConfigureLoggerService(this IServiceCollection services)
    {
        services.AddScoped<IRepositoryManager, IRepositoryManager>();
    }
}