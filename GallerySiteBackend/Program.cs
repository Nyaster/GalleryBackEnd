using Application.BackgroundService;
using GallerySiteBackend.Configuration;
using GallerySiteBackend.Extensions;
using GallerySiteBackend.Presentation;
using Microsoft.AspNetCore.Mvc;
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
        builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: true);
        builder.Services.AddOptions<JwtConfiguration>().Bind(builder.Configuration.GetSection("JwtConfig"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<ParserSettings>().Bind(builder.Configuration.GetSection("ParserSettings"))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.ConfigureNpsqlContext(builder.Configuration);
        builder.Services.ConfigureLoggerService();
        builder.Services.ConfigureRepositoryManager();
        builder.Services.ConfigureServicesInjection();
        builder.Services.AddSingleton<IImageEmbeddingGenerator, OnnxImageEmbeddingGenerator>();
        builder.Services.AddHostedService<ImageEmbeddingPollingService>();
        // Add services to the container.
        builder.Services.AddAutoMapper(_ => { }, typeof(Program).Assembly);
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
        builder.Services.ConfigureJwtToken(builder.Configuration);
        builder.Services.ConfigureAuthorizationPolicies();

        builder.Services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(Application.AssemblyApplication).Assembly));
        var app = builder.Build();
        app.UseExceptionHandler();
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors("CorsPolicy");
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }
}
