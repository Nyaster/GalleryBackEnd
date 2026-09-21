using Entities.Models;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Repository;
using Service;
using Testcontainers.PostgreSql;

namespace GallerySiteIntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public string RootPath { get; } =
        Path.Combine(Path.GetTempPath(), "gallery-auth-tests-" + Guid.NewGuid().ToString("N"));

    public string KeyPath => Path.Combine(RootPath, "keys");
    public string ConnectionString => _database.GetConnectionString();

    public static JwtConfiguration Jwt { get; } = new()
    {
        ValidIssuer = "https://gallery.test", ValidAudience = "https://gallery.test",
        SecretKey = "integration-tests-only-key-more-than-32-bytes", AccessTokenMinutes = 15, RefreshTokenDays = 7
    };

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(KeyPath);
        await _database.StartAsync();
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
    }

    public RepositoryContext CreateContext(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<RepositoryContext>()
            .UseNpgsql(ConnectionString, postgres => postgres.UseVector()).AddInterceptors(interceptors).Options);

    public AuthenticationService Authentication(RepositoryContext context, TimeProvider clock)
        => new(new RepositoryManager(context), new PasswordHasher<AppUser>(), Options.Create(Jwt), clock,
            NullLoggerFactory.Instance);

    public AuthenticatorService Authenticator(RepositoryContext context, TimeProvider clock)
        => new(new RepositoryManager(context), new PasswordHasher<AppUser>(),
            DataProtectionProvider.Create(new DirectoryInfo(KeyPath),
                builder => builder.SetApplicationName("Lilgallery")),
            clock, NullLogger<AuthenticatorService>.Instance);

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(RootPath, recursive: true);
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow.AddMinutes(-2);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}