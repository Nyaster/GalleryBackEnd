using Application.BackgroundService;
using Contracts;
using GallerySiteBackend;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Extensions;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.Extensions.Logging;
using Repository;
using Service;
using Service.Contracts;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);

var nlogConfiguration = new NLog.Config.XmlLoggingConfiguration(Path.Combine(builder.Environment.ContentRootPath, "nlog.config"));
nlogConfiguration.Variables["logDirectory"] = builder.Configuration["Observability:LogPath"] ?? "/app/logs";
nlogConfiguration.Variables["retentionDays"] = builder.Configuration["Observability:RetentionDays"] ?? "30";
LogManager.Configuration = nlogConfiguration;
builder.Logging.AddNLog();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddApplicationPart(typeof(AssemblyReference).Assembly);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));
builder.Services.AddOptions<ObservabilityOptions>().Bind(builder.Configuration.GetSection("Observability"))
    .ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddOptions<JwtConfiguration>().Bind(builder.Configuration.GetSection("JwtConfig"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ImageStorageOptions>().Bind(builder.Configuration.GetSection("ImageStorage"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EmbeddingOptions>().Bind(builder.Configuration.GetSection("Embedding"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ParserSettings>().Bind(builder.Configuration.GetSection("ParserSettings"));
builder.Services.AddOptions<BootstrapAdminOptions>().Bind(builder.Configuration.GetSection("BootstrapAdmin"));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit =
    (builder.Configuration.GetValue<int?>("ImageStorage:MaximumUploadMegabytes") ?? 20) * 1024L * 1024L);

builder.Services.AddDbContext<RepositoryContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), postgres => postgres.UseVector()));
builder.Services.ConfigureRepositoryManager();
builder.Services.ConfigureServicesInjection();
builder.Services.AddHttpClient();
builder.Services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Application.AssemblyApplication).Assembly));
builder.Services.ConfigureCors(builder.Configuration);
builder.Services.ConfigureJwtToken(builder.Configuration);
builder.Services.AddAuthorization(options => options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin")));
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, SecurityAuthorizationMiddlewareResultHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var value in builder.Configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>() ?? [])
        if (System.Net.IPAddress.TryParse(value, out var address)) options.KnownProxies.Add(address);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategories.Security);
        logger.LogWarning("RateLimitRejected {TimestampUtc} {TraceId} {Method} {Route} {ClientIp}",
            DateTimeOffset.UtcNow, context.HttpContext.TraceIdentifier, context.HttpContext.Request.Method,
            context.HttpContext.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
                ? endpoint.RoutePattern.RawText ?? context.HttpContext.Request.Path.Value ?? "/"
                : context.HttpContext.Request.Path.Value ?? "/",
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("comment-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

if (builder.Configuration.GetSection("Embedding").Get<EmbeddingOptions>()?.Enabled == true)
{
    builder.Services.AddSingleton<IImageEmbeddingGenerator, OnnxImageEmbeddingGenerator>();
    builder.Services.AddHostedService<ImageEmbeddingPollingService>();
}
else
{
    builder.Services.AddSingleton<IImageEmbeddingGenerator, DisabledImageEmbeddingGenerator>();
}
builder.Services.AddHostedService<ScrapeRunPollingService>();
builder.Services.AddHostedService<RankingSnapshotPollingService>();
builder.Services.AddHostedService<RefreshSessionCleanupService>();

var app = builder.Build();
if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await DatabaseMigrationService.MigrateAsync(app.Services, app.Logger, CancellationToken.None);
    return;
}
app.UseExceptionHandler();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseCors("CorsPolicy");
app.UseRouting();
app.UseMiddleware<ApiRequestObservabilityMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

public partial class Program;
