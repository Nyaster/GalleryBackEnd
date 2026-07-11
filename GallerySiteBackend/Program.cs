using Application.BackgroundService;
using Contracts;
using GallerySiteBackend;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Extensions;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Repository;
using Service;
using Service.Contracts;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddControllers().AddApplicationPart(typeof(AssemblyReference).Assembly);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddSingleton(TimeProvider.System);

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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("upload", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<DatabaseMigrationService>();

if (builder.Configuration.GetSection("Embedding").Get<EmbeddingOptions>()?.Enabled == true)
{
    builder.Services.AddSingleton<IImageEmbeddingGenerator, OnnxImageEmbeddingGenerator>();
    builder.Services.AddHostedService<ImageEmbeddingPollingService>();
}
builder.Services.AddHostedService<ScrapeRunPollingService>();

var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.UseCors("CorsPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.Run();

public partial class Program;
