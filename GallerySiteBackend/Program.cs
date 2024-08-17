using System.Text;
using GallerySiteBackend.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NLog;

namespace GallerySiteBackend;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        LogManager.Setup().LoadConfigurationFromFile(string.Concat(Directory.GetCurrentDirectory(),
            "/nlog.config"));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("secrets.json", optional: true, reloadOnChange: true).Build();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.ConfigureNpsqlContext(builder.Configuration);
        builder.Services.ConfigureLoggerService();
        builder.Services.ConfigureRepositoryManager();
        builder.Services.ConfigureServiceManager();
        // Add services to the container.
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Presentation.AssemblyReference).Assembly);
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.ConfigureCors();
        builder.Services.AddAutoMapper(typeof(Program));
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        #region JwtConfiguration

        builder.Services.AddAuthentication(option =>
        {
            option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidIssuer = builder.Configuration["Issuer"],
                ValidAudience = builder.Configuration["Issuer"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["SecretKey"]))
            };
            opt.Events = new JwtBearerEvents()
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                    {
                        context.Response.Headers["Token-expired"] = "true";
                    }

                    return Task.CompletedTask;
                }
            };
        }).AddJwtBearer("IgnoreTokenExpirationScheme", opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, //by who
                ValidateAudience = true, //for whom
                ValidateLifetime = false,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidIssuer = builder.Configuration["Issuer"], //should come from configuration
                ValidAudience = builder.Configuration["Issuer"], //should come from configuration
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["SecretKey"]))
            };
        });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("Admin"));
            options.AddPolicy("RequireUserRole", policy => policy.RequireRole("User"));
            options.AddPolicy("RequireModeratorRole", policy => policy.RequireRole("Moderator"));
        });

        #endregion


        var app = builder.Build();
        /*var logger = app.Services.GetRequiredService<ILoggerManager>();
        app.ConfigureExceptionHandler(logger);*/
        // Configure the HTTP request pipeline.
        if (app.Environment.IsProduction())
        {
            app.UseHsts();
        }
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("CorsPolicy");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHttpsRedirection();

        app.UseAuthorization();


        app.MapControllers();
        app.Run();
    }
}