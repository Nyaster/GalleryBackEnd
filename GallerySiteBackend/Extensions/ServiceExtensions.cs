using System.Text;
using GallerySiteBackend;
using Contracts;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Repository;

namespace GallerySiteBackend.Extensions;

public static class ServiceExtensions
{
    public static void ConfigureRepositoryManager(this IServiceCollection services)
        => services.AddScoped<IRepositoryManager, RepositoryManager>();

    public static void ConfigureCors(this IServiceCollection services, IConfiguration configuration)
    {
        var productionOrigin = configuration["Cors:ProductionOrigin"];
        var origins = !string.IsNullOrWhiteSpace(productionOrigin)
            ? [productionOrigin]
            : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy("CorsPolicy", policy =>
        {
            if (origins.Length == 0) return;
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
    }

    public static void ConfigureJwtToken(this IServiceCollection services, IConfiguration configuration)
    {
        var config = configuration.GetSection("JwtConfig").Get<JwtConfiguration>()
            ?? throw new InvalidOperationException("JwtConfig is required.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.ValidIssuer,
                ValidateAudience = true,
                ValidAudience = config.ValidAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SecretKey)),
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategories.Security);
                    logger.LogWarning("JwtAuthenticationFailed {TimestampUtc} {TraceId} {Method} {Route} {ClientIp}",
                        DateTimeOffset.UtcNow, context.HttpContext.TraceIdentifier, context.Request.Method,
                        context.HttpContext.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
                            ? endpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "/"
                            : context.Request.Path.Value ?? "/",
                        context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                    return Task.CompletedTask;
                }
            };
        });
    }
}
