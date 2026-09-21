using Entities.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Service.Contracts;

namespace Service;

public static class ServicesDiInjectionConfiguration
{
    public static void ConfigureServicesInjection(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAuthenticatorService, AuthenticatorService>();
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<IImageStorage, LocalImageStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IImageDerivativeCache, LocalImageDerivativeCache>();
        services.AddScoped<IImageParserService, AppImageParserService>();
    }
}