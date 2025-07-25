using System.Text;
using Application.BackgroundService;
using Contracts;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Extensions;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Service;
using Service.Contracts;

namespace GallerySiteBackend;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddResponseCaching();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("secrets.json", true, true).Build();

        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddOptions<JwtConfiguration>().Bind(builder.Configuration.GetSection("JwtConfig"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<ParserSettings>().Bind(builder.Configuration.GetSection("ParserSettings"));
        var jwtConfiguration = builder.Configuration.GetSection("JwtConfig1").Get<JwtConfiguration>();
        ;
        builder.Services.ConfigureNpsqlContext(builder.Configuration);
        builder.Services.ConfigureLoggerService();
        builder.Services.ConfigureRepositoryManager();
        builder.Services.ConfigureServicesInjection();
        builder.Services.AddSingleton<IImageEmbeddingGenerator, OnnxImageEmbeddingGenerator>();
        builder.Services.AddHostedService<ImageEmbeddingPollingService>();
        // Add services to the container.
        builder.Services.AddAutoMapper(typeof(Program));
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AssemblyReference).Assembly);
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(name: "Bearer", securityScheme: new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter the Bearer Authorization string as following: `Bearer Generated-JWT-Token`",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Name = "Bearer",
                        In = ParameterLocation.Header,
                        Reference = new OpenApiReference
                        {
                            Id = "Bearer",
                            Type = ReferenceType.SecurityScheme
                        }
                    },
                    new List<string>()
                }
            });
        });
        builder.Services.ConfigureCors();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.ConfigureJwtToken(configuration);
        builder.Services.ConfigureAuthorizationPolicies();

        builder.Services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(Application.AssemblyApplication).Assembly));
        var app = builder.Build();
        app.UseExceptionHandler(opt => { });
        // Configure the HTTP request pipeline.
        /*if (app.Environment.IsProduction())
        {
            app.UseHsts();
        }*/

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("CorsPolicy");
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHttpsRedirection();
        app.MapControllers();
        app.Run();
    }
}