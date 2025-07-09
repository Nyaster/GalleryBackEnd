using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Service.Contracts;

namespace Service;

public static class ServicesDiInjectionConfiguration
{
    public static void ConfigureServicesInjection(this IServiceCollection services)
    {
        services.AddScoped<IAppAdministrationService, AppAdministratorService>();
        services.AddScoped<IImageParserService, AppImageParserService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IAppImageService, AppImageService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IServiceManager, ServiceManager>();
    }
}