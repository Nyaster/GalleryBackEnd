using System.Data.Common;
using Entities.Models;
using GallerySiteBackend;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Repository;
using Service;
using Pgvector;

namespace GallerySiteIntegrationTests;

public sealed class ForMeMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string PreviousMigration = "20261024144932_AddForMeFeeds";

    [Fact]
    public async Task Migration_PopulatedDatabase_UpDownKeepRowsAndAllowWorkBetweenCommands()
    {
        var observer = new MigrationObserver(fixture);
        await using var context = fixture.CreateContext(observer);
        context.AppUsers.Add(new AppUser { Login = "migration-existing", NormalizedLogin = "MIGRATION-EXISTING",
            PasswordHash = "", CreatedAtUtc = DateTimeOffset.UtcNow });
        var image = Image("migration-existing");
        context.Images.Add(image);
        await context.SaveChangesAsync();
        // The interceptor performs ordinary reads/inserts after ALTER and before concurrent REINDEX.
        observer.ProbeBetweenCommands = true;
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await AssertIndexAsync(context, 64);
        await context.Database.MigrateAsync();
        await AssertIndexAsync(context, 128);
        Assert.Equal(2, observer.BetweenCommandProbes);
        Assert.True(await context.AppUsers.AnyAsync(user => user.Login == "migration-existing"));
        Assert.Equal(2, await context.AppUsers.CountAsync(user => user.Login.StartsWith("migration-probe-")));
        Assert.True(await context.Images.AnyAsync(candidate => candidate.Id == image.Id && candidate.Embedding != null));
        Assert.Equal(2, await context.Images.CountAsync(candidate => candidate.StorageKey.StartsWith("migration-probe-")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationService_OverrideOnlyDuringMigration_RestoresPriorTimeout(bool fail)
    {
        var observer = new MigrationObserver(fixture) { FailMigration = fail };
        await using var context = fixture.CreateContext(observer);
        context.Database.SetCommandTimeout(37);
        if (!fail)
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            observer.MigrationTimeouts.Clear();
            observer.RebuildTimeouts.Clear();
        }
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddLogging();
        services.AddOptions<DatabaseMigrationOptions>().Configure(options => options.CommandTimeoutSeconds = 73)
            .ValidateDataAnnotations();
        services.AddSingleton<IOptions<BootstrapAdminOptions>>(Options.Create(new BootstrapAdminOptions
        {
            Login = "migration-bootstrap", Password = "test-bootstrap-password"
        }));
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        await using var provider = services.BuildServiceProvider();
        if (fail)
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseMigrationService.MigrateAsync(provider, NullLogger.Instance, default));
        else
            await DatabaseMigrationService.MigrateAsync(provider, NullLogger.Instance, default);
        Assert.Equal(37, context.Database.GetCommandTimeout());
        Assert.Contains(73, observer.MigrationTimeouts);
        if (!fail)
        {
            Assert.Equal(new[] { 73, 73 }, observer.RebuildTimeouts);
            Assert.Contains(37, observer.BootstrapTimeouts);
        }
        await using var normal = fixture.CreateContext();
        Assert.Null(normal.Database.GetCommandTimeout());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MigrationService_InvalidOptions_DirectInvocationRejectsBeforeDatabaseCommands(int timeout)
    {
        var observer = new MigrationObserver(fixture);
        await using var context = fixture.CreateContext(observer);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddOptions<DatabaseMigrationOptions>().Configure(options => options.CommandTimeoutSeconds = timeout)
            .ValidateDataAnnotations();
        services.AddSingleton<IOptions<BootstrapAdminOptions>>(Options.Create(new BootstrapAdminOptions()));
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<OptionsValidationException>(() => DatabaseMigrationService.MigrateAsync(provider, NullLogger.Instance, default));
        Assert.Empty(observer.MigrationTimeouts);
    }

    private static UserMadeImage Image(string key) => new()
    {
        StorageKey = key, ContentType = "image/jpeg", Width = 1, Height = 1,
        UploadedAtUtc = DateTimeOffset.UtcNow, Visibility = ImageVisibility.Gallery,
        ModerationStatus = ModerationStatus.Approved, EmbeddingStatus = EmbeddingStatus.Ready,
        Embedding = new Vector(Enumerable.Repeat(0.1f, 1024).ToArray())
    };

    private static async Task AssertIndexAsync(RepositoryContext context, int effort)
    {
        var valid = await context.Database.SqlQueryRaw<bool>("""
            SELECT i.indisvalid AS "Value" FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
            WHERE c.relname='IX_Images_Embedding'
            """).SingleAsync();
        Assert.True(valid);
        var definition = await context.Database.SqlQueryRaw<string>("""
            SELECT pg_get_indexdef(c.oid) AS "Value" FROM pg_class c WHERE c.relname='IX_Images_Embedding'
            """).SingleAsync();
        Assert.Contains("USING hnsw", definition);
        Assert.Contains("vector_l2_ops", definition);
        Assert.Contains($"ef_construction='{effort}'", definition);
        Assert.Contains("WHERE (\"Embedding\" IS NOT NULL)", definition);
    }

    private sealed class MigrationObserver(PostgresFixture fixture) : DbCommandInterceptor
    {
        public bool ProbeBetweenCommands { get; set; }
        public bool FailMigration { get; init; }
        public int BetweenCommandProbes { get; private set; }
        public List<int> MigrationTimeouts { get; } = [];
        public List<int> BootstrapTimeouts { get; } = [];
        public List<int> RebuildTimeouts { get; } = [];

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ALTER INDEX") || command.CommandText.Contains("REINDEX INDEX CONCURRENTLY"))
                RebuildTimeouts.Add(command.CommandTimeout);
            if (ProbeBetweenCommands && command.CommandText.Contains("REINDEX INDEX CONCURRENTLY"))
            {
                await using var probe = fixture.CreateContext();
                probe.AppUsers.Add(new AppUser { Login = $"migration-probe-{BetweenCommandProbes}",
                    NormalizedLogin = $"MIGRATION-PROBE-{BetweenCommandProbes}", PasswordHash = "",
                    CreatedAtUtc = DateTimeOffset.UtcNow });
                probe.Images.Add(Image($"migration-probe-{BetweenCommandProbes}"));
                await probe.SaveChangesAsync(cancellationToken);
                Assert.True(await probe.AppUsers.AnyAsync(user => user.Login == "migration-existing", cancellationToken));
                BetweenCommandProbes++;
            }
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("__EFMigrationsHistory"))
            {
                MigrationTimeouts.Add(command.CommandTimeout);
                if (FailMigration) throw new InvalidOperationException("Injected migration failure");
            }
            if (command.CommandText.Contains("FROM \"AppUsers\"")) BootstrapTimeouts.Add(command.CommandTimeout);
            return ValueTask.FromResult(result);
        }
    }
}
