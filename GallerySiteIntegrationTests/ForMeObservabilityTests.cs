using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contracts;
using Entities.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NLog;
using Pgvector;
using Repository;
using Shared.DataTransferObjects;
using Handler = Application.Features.Images.GetForMe.Handler;
using Command = Application.Features.Images.GetForMe.Command;
using Service.Contracts;

namespace GallerySiteIntegrationTests;

[CollectionDefinition("For me observability", DisableParallelization = true)]
public sealed class ForMeObservabilityCollection;

[Collection("For me observability")]
public sealed class ForMeObservabilityTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string Origin = "https://observability-client.test";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Api_CreateContinueAndConcurrentRequests_EmitUniqueCorrelatedSafeJsonAndExposeHeader()
    {
        var seed = await SeedAsync();
        await using var parent = new ApiFactory(database, seed.Clock);
        await using var app = parent.WithWebHostBuilder(builder => builder.UseSetting("Cors:ProductionOrigin", Origin));
        using var client = Client(app, seed.Token);
        client.DefaultRequestHeaders.Add("Origin", Origin);
        client.DefaultRequestHeaders.Add("X-Request-ID", "spoof-secret");
        using var initial = await client.GetAsync("/api/images/for-me?pageSize=1&excludedTags=tag-secret");
        initial.EnsureSuccessStatusCode();
        var first = (await initial.Content.ReadFromJsonAsync<ForMeFeedDto>(JsonOptions))!;
        var id = RequestId(initial);
        Assert.NotEqual("spoof-secret", id);
        Assert.Contains("X-Request-ID", string.Join(',', initial.Headers.GetValues("Access-Control-Expose-Headers")));
        var creation = await EventsAsync(id);
        Assert.Equal(3, Summaries(creation).Count);
        Assert.Single(creation, entry => Event(entry) == "ForMeFeedCreated");
        Assert.All(creation, entry => Assert.Equal("CreateFeed", entry.GetProperty("Operation").GetString()));
        var created = creation.Single(entry => Event(entry) == "ForMeFeedCreated");
        Assert.Equal("Publication", created.GetProperty("Stage").GetString());
        Assert.Equal(1, created.GetProperty("SeedCount").GetInt32());
        Assert.Equal(1, created.GetProperty("RepresentativeCount").GetInt32());
        Assert.Equal(1, created.GetProperty("NeighborQueryCount").GetInt32());
        Assert.Equal(4, created.GetProperty("StoredImageCount").GetInt32());
        Assert.False(created.TryGetProperty("FallbackQueryElapsedMilliseconds", out _));
        foreach (var name in new[] { "GenerationSlot", "VectorSearchSetup", "ExcludedTagResolution", "SeedLoad", "RepresentativeSelection", "NeighborQueries", "Merge", "GenerationCommit", "Publication" })
            Assert.Equal(JsonValueKind.Number, created.GetProperty(name + "ElapsedMilliseconds").ValueKind);
        foreach (var result in Summaries(creation).Where(entry => Event(entry) != "ForMeFeedCreated"))
        {
            Assert.Equal("Succeeded", result.GetProperty("Outcome").GetString());
            Assert.Equal("INFO", result.GetProperty("level").GetString());
            Assert.Equal("Personalized", result.GetProperty("Mode").GetString());
            Assert.Equal(1, result.GetProperty("ReturnedImageCount").GetInt32());
            Assert.True(result.GetProperty("HasMore").GetBoolean());
            if (Event(result) == "ForMeFeedPageRead")
                foreach (var field in new[] { "ConfiguredRepresentativeCount", "ResolvedExcludedTagCount", "AnnCandidateLimit", "AnnCandidateRowCount", "ExactTopUpQueryCount", "ExactTopUpImageCount", "AppliedAnnRandomPageCost" })
                    Assert.False(result.TryGetProperty(field, out _));
            Assert.Equal(Event(result) == "ForMeRequestHandled" ? "BuildResponse" : "ReadCommit", result.GetProperty("Stage").GetString());
        }
        ValidateJson(creation);
        var samples = Summaries(creation).Select(entry =>
        {
            var sample = System.Text.Json.Nodes.JsonNode.Parse(entry.GetRawText())!.AsObject();
            sample["RequestId"] = "sample-server-request";
            sample["timestampUtc"] = "2026-10-24T00:00:00Z";
            foreach (var name in new[] { "ConnectionId", "SpanId", "ParentId" }) sample.Remove(name);
            return sample.ToJsonString();
        });
        File.WriteAllLines("/tmp/gallery-for-me-observability-samples.jsonl", samples);
        Assert.DoesNotContain("tag-secret", string.Join('\n', Summaries(creation)));
        Assert.DoesNotContain("spoof-secret", string.Join('\n', Summaries(creation)));

        var responses = await Task.WhenAll(
            client.GetAsync("/api/images/for-me"),
            client.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(first.NextCursor!)),
            client.GetAsync("/api/images/for-me?cursor=cursor-secret"),
            client.GetAsync("/api/images/" + seed.ImageId));
        try
        {
            Assert.Equal(responses.Length, responses.Select(RequestId).Distinct().Count());
            for (var index = 0; index < responses.Length; index++)
            {
                await responses[index].Content.ReadAsStringAsync();
                var entries = await EventsAsync(RequestId(responses[index]));
                ValidateJson(entries);
                var expectedOperation = index == 0 ? "CreateFeed" : "ReadFeedPage";
                if (index == 3)
                    Assert.All(entries, entry => Assert.False(entry.TryGetProperty("Operation", out _)));
                else
                    Assert.All(entries, entry => Assert.Equal(expectedOperation, entry.GetProperty("Operation").GetString()));
                if (index == 1)
                {
                    Assert.Equal(2, Summaries(entries).Count);
                    Assert.DoesNotContain(entries, entry => Event(entry) == "ForMeFeedCreated");
                    Assert.DoesNotContain(first.NextCursor!, string.Join('\n', Summaries(entries)));
                }
            }
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GallerySiteBackend.ScopeProbe")
                .LogInformation("BackgroundScopeProbe");
            LogManager.Flush();
            var probe = ReadAll().Last(entry => entry.GetProperty("message").GetString() == "BackgroundScopeProbe");
            Assert.False(probe.TryGetProperty("RequestId", out _));
            Assert.False(probe.TryGetProperty("Operation", out _));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Api_BindingAndDomainErrors_KeepCanonicalHeaderAndOperationUnderW3CActivity()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = app.Client();
        using (var unauthenticated = await client.GetAsync("/api/images/for-me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.All(await EventsAsync(RequestId(unauthenticated)), entry =>
                Assert.Equal("CreateFeed", entry.GetProperty("Operation").GetString()));
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", seed.Token);
        using var activity = new Activity("binding-parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        client.DefaultRequestHeaders.Add("traceparent", activity.Id);
        foreach (var path in new[] { "?pageSize=invalid", "?cursor=", "?cursor=cursor-secret" })
        {
            using var response = await client.GetAsync("/api/images/for-me" + path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var id = RequestId(response);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            Assert.Equal(id, problem.Extensions["traceId"]?.ToString());
            Assert.NotEqual(activity.Id, id);
            var entries = await EventsAsync(id);
            ValidateJson(entries);
            Assert.All(entries, entry => Assert.Equal(path.Contains("cursor") ? "ReadFeedPage" : "CreateFeed",
                entry.GetProperty("Operation").GetString()));
        }
        using var initial = await client.GetAsync("/api/images/for-me?pageSize=1");
        var first = (await initial.Content.ReadFromJsonAsync<ForMeFeedDto>(JsonOptions))!;
        seed.Clock.Advance(TimeSpan.FromHours(1));
        using var expired = await client.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(first.NextCursor!));
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        var expiredId = RequestId(expired);
        Assert.Equal(expiredId, (await expired.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["traceId"]?.ToString());
        ValidateJson(await EventsAsync(expiredId));
    }

    [Fact]
    public async Task Api_GenerationCapacityAndRateLimit_RejectionsRemainCorrelatedWithoutFalseMode()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = Client(app, seed.Token);
        await using (var holder = database.CreateContext())
        await using (var transaction = await holder.Database.BeginTransactionAsync())
        {
            for (var slot = 0; slot < 2; slot++)
                Assert.True(await holder.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({ForMeFeedRepository.GenerationLockNamespace}, {slot}) AS \"Value\"").SingleAsync());
            using var rejected = await client.GetAsync("/api/images/for-me");
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            var id = RequestId(rejected);
            Assert.Equal(id, (await rejected.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["traceId"]?.ToString());
            var entries = await EventsAsync(id);
            var created = Assert.Single(entries, entry => Event(entry) == "ForMeFeedCreated");
            Assert.Equal("GenerationSlot", created.GetProperty("Stage").GetString());
            Assert.Equal("Rejected", created.GetProperty("Outcome").GetString());
            Assert.Equal("WARN", created.GetProperty("level").GetString());
            Assert.False(created.TryGetProperty("Mode", out _));
            Assert.False(created.TryGetProperty("SeedCount", out _));
            Assert.False(created.TryGetProperty("StoredImageCount", out _));
            Assert.False(created.TryGetProperty("SeedLoadElapsedMilliseconds", out _));
            Assert.Equal(2, Summaries(entries).Count);
            ValidateJson(entries);
        }
        HttpResponseMessage? limited = null;
        for (var index = 0; index < 11; index++)
        {
            var response = await client.GetAsync("/api/images/for-me");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = response; break; }
            response.EnsureSuccessStatusCode();
            response.Dispose();
        }
        using (limited)
        {
            Assert.NotNull(limited);
            var id = RequestId(limited);
            Assert.Equal(id, (await limited.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["traceId"]?.ToString());
            var entries = await EventsAsync(id);
            Assert.Empty(Summaries(entries));
            ValidateJson(entries);
            Assert.All(entries, entry => Assert.Equal("CreateFeed", entry.GetProperty("Operation").GetString()));
        }
    }

    [Theory]
    [InlineData("ExcludedTagResolution", "ForMeExcludedTagResolution", "ForMeFeedCreated")]
    [InlineData("NeighborQueries", "ForMeAnnNeighbors", "ForMeFeedCreated")]
    [InlineData("Publication", "FOR UPDATE", "ForMeFeedCreated")]
    [InlineData("CardHydration", "\"Width\"", "ForMeFeedPageRead")]
    public async Task Api_RepositoryFailure_EmitsSafePartialStageAndPreservesGlobalErrorCorrelation(string stage, string marker, string eventName)
    {
        var seed = await SeedAsync();
        await using var parent = new ApiFactory(database, seed.Clock);
        await using var app = parent.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<RepositoryContext>();
            services.AddDbContext<RepositoryContext>(options => options.UseNpgsql(database.ConnectionString, postgres => postgres.UseVector())
                .AddInterceptors(new FailCommand(marker)));
        }));
        using var client = Client(app, seed.Token);
        using var response = await client.GetAsync("/api/images/for-me?excludedTags=tag-secret");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var id = RequestId(response);
        Assert.Equal(id, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["traceId"]?.ToString());
        var entries = await EventsAsync(id);
        var created = Assert.Single(entries, entry => Event(entry) == eventName);
        Assert.Equal(JsonValueKind.Number, created.GetProperty(stage + "ElapsedMilliseconds").ValueKind);
        Assert.Equal(stage, created.GetProperty("Stage").GetString());
        Assert.Equal("Failed", created.GetProperty("Outcome").GetString());
        Assert.Equal("ERROR", created.GetProperty("level").GetString());
        Assert.Equal("InvalidOperationException", created.GetProperty("FailureType").GetString());
        Assert.False(created.TryGetProperty("StoredImageCount", out _));
        Assert.Equal(stage == "Publication", created.TryGetProperty("Mode", out _));
        if (stage == "NeighborQueries") Assert.Equal(0, created.GetProperty("NeighborQueryCount").GetInt32());
        Assert.DoesNotContain("exception-secret", string.Join('\n', Summaries(entries)));
        Assert.DoesNotContain("tag-secret", string.Join('\n', Summaries(entries)));
        ValidateJson(entries);
    }

    [Fact]
    public async Task Api_ServerDeadline_CorrelatesCanceledRepositoryAndDeadlineExceededHandler()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, new DeadlineClock(seed.Clock));
        using var client = Client(app, seed.Token);
        using var response = await client.GetAsync("/api/images/for-me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var id = RequestId(response);
        Assert.Equal(id, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions["traceId"]?.ToString());
        var entries = await EventsAsync(id);
        Assert.Equal("Canceled", Assert.Single(entries, entry => Event(entry) == "ForMeFeedCreated").GetProperty("Outcome").GetString());
        var handled = Assert.Single(entries, entry => Event(entry) == "ForMeRequestHandled");
        Assert.Equal("DeadlineExceeded", handled.GetProperty("Outcome").GetString());
        Assert.Equal("CreateFeed", handled.GetProperty("Stage").GetString());
        Assert.False(handled.TryGetProperty("ReadFeedPageElapsedMilliseconds", out _));
        ValidateJson(entries);
    }

    [Fact]
    public async Task Api_FallbackAndMissingSnapshot_LogOnlyKnownCountersAndEnteredStages()
    {
        var seed = await SeedAsync();
        await using (var context = database.CreateContext()) await context.ImageLikes.ExecuteDeleteAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var client = Client(app, seed.Token);
        using var fallback = await client.GetAsync("/api/images/for-me");
        fallback.EnsureSuccessStatusCode();
        await fallback.Content.ReadAsStringAsync();
        var entries = await EventsAsync(RequestId(fallback));
        var created = Assert.Single(entries, entry => Event(entry) == "ForMeFeedCreated");
        Assert.Equal("Fallback", created.GetProperty("Mode").GetString());
        Assert.Equal(0, created.GetProperty("SeedCount").GetInt32());
        Assert.Equal(0, created.GetProperty("RepresentativeCount").GetInt32());
        Assert.Equal(0, created.GetProperty("NeighborQueryCount").GetInt32());
        Assert.Equal(JsonValueKind.Number, created.GetProperty("FallbackQueryElapsedMilliseconds").ValueKind);
        ValidateJson(entries);
        var cursor = app.Services.GetRequiredService<IForMeFeedCursorProtector>().Protect(new ForMeFeedCursor(
            1, Guid.NewGuid(), seed.UserId, 0, 20, seed.Clock.GetUtcNow().AddHours(1)));
        using var missing = await client.GetAsync("/api/images/for-me?cursor=" + Uri.EscapeDataString(cursor));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        var failed = await EventsAsync(RequestId(missing));
        var read = Assert.Single(failed, entry => Event(entry) == "ForMeFeedPageRead");
        Assert.Equal("SnapshotLoad", read.GetProperty("Stage").GetString());
        Assert.Equal("Rejected", read.GetProperty("Outcome").GetString());
        Assert.False(read.TryGetProperty("Mode", out _));
        Assert.False(read.TryGetProperty("ReturnedImageCount", out _));
        Assert.False(read.TryGetProperty("HasMore", out _));
        Assert.False(read.TryGetProperty("PageSelectionElapsedMilliseconds", out _));
        ValidateJson(failed);
    }

    [Fact]
    public async Task DirectInvocation_HandlerSuppliesOnlyOperationAndDisposesItBeforeDirectRepositoryRead()
    {
        var seed = await SeedAsync();
        await using var app = new ApiFactory(database, seed.Clock);
        using var scope = app.Services.CreateScope();
        var before = ReadAll().Select(entry => entry.GetRawText()).ToHashSet();
        var services = scope.ServiceProvider;
        var repository = services.GetRequiredService<IForMeFeedRepository>();
        var handler = new Handler(repository, services.GetRequiredService<IForMeFeedCursorProtector>(),
            new DirectUser(seed.UserId), seed.Clock, services.GetRequiredService<ILogger<Handler>>());
        await handler.Handle(new Command(), default);
        await Assert.ThrowsAsync<Entities.Exceptions.Base409ConflictException>(() =>
            repository.ReadAsync(Guid.NewGuid(), new(seed.UserId, false), 0));
        var entries = Summaries(ReadAll().Where(entry => !before.Contains(entry.GetRawText())));
        Assert.Equal(4, entries.Count);
        Assert.All(entries, entry => Assert.False(entry.TryGetProperty("RequestId", out _)));
        Assert.All(entries.Take(3), entry => Assert.Equal("CreateFeed", entry.GetProperty("Operation").GetString()));
        Assert.False(entries[^1].TryGetProperty("Operation", out _));
        Assert.Equal("Rejected", entries[^1].GetProperty("Outcome").GetString());
    }

    private sealed class DirectUser(int id) : IUserContext
    {
        public int? UserId => id;
        public string? Login => "unused";
        public bool IsInRole(string role) => false;
        public void RequireAuthenticated() { }
    }

    private static HttpClient Client(WebApplicationFactory<Program> app, string token)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://gallery.test"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static string RequestId(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("X-Request-ID"));
    private static string? Event(JsonElement entry) => entry.TryGetProperty("EventName", out var value) ? value.GetString() : null;
    private static List<JsonElement> Summaries(IEnumerable<JsonElement> entries) => entries.Where(entry => Event(entry) is not null).ToList();

    private static void ValidateJson(IEnumerable<JsonElement> entries)
    {
        foreach (var entry in entries)
        {
            Assert.All(entry.EnumerateObject().GroupBy(property => property.Name), group => Assert.Single(group));
            Assert.Equal(JsonValueKind.String, entry.GetProperty("RequestId").ValueKind);
            if (entry.TryGetProperty("TraceId", out var traceId))
                Assert.Equal(entry.GetProperty("RequestId").GetString(), traceId.GetString());
            if (Event(entry) is null) continue;
            Assert.False(entry.TryGetProperty("TraceId", out _));
            foreach (var property in entry.EnumerateObject().Where(property => property.Name.EndsWith("Milliseconds")))
            {
                Assert.Equal(JsonValueKind.Number, property.Value.ValueKind);
                Assert.True(property.Value.GetDouble() >= 0);
            }
            foreach (var forbidden in new[] { "UserId", "Login", "FeedId", "Cursor", "Tags", "Filters", "Embedding", "QueryString" })
                Assert.False(entry.TryGetProperty(forbidden, out _));
        }
    }

    private List<JsonElement> ReadAll()
    {
        LogManager.Flush();
        var paths = new[] { "application.jsonl", "security-audit.jsonl" }
            .Select(name => Path.Combine(database.RootPath, "logs", name)).Where(File.Exists);
        return paths.SelectMany(File.ReadAllLines).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToList();
    }

    private async Task<List<JsonElement>> EventsAsync(string requestId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var entries = ReadAll().Where(entry => entry.TryGetProperty("RequestId", out var id) && id.GetString() == requestId).ToList();
            if (entries.Any(entry => entry.GetProperty("message").GetString()!.StartsWith("ApiRequestCompleted"))) return entries;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException("No correlated completion event for server request ID " + requestId);
    }

    private async Task<(TestClock Clock, string Token, int ImageId, int UserId)> SeedAsync()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        await context.Images.ExecuteDeleteAsync();
        await context.ForMeFeeds.ExecuteDeleteAsync();
        await context.AppUsers.ExecuteDeleteAsync();
        var account = await database.Authentication(context, clock).RegisterAsync(new("observability" + Guid.NewGuid().ToString("N"), "integration-password"));
        var images = Enumerable.Range(0, 5).Select(index => new UserMadeImage
        {
            UploadedById = account.Response.User.Id, Source = ImageSource.UserUpload, UploadedAtUtc = clock.GetUtcNow(),
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            AiUsage = AiUsageClassification.HumanMade, StorageKey = "observability/" + index,
            ContentType = "image/jpeg", Width = 1, Height = 1,
            Embedding = new Vector(Enumerable.Range(0, 1024).Select(_ => (float)index).ToArray()), EmbeddingStatus = EmbeddingStatus.Ready
        }).ToArray();
        context.Images.AddRange(images);
        await context.SaveChangesAsync();
        context.ImageLikes.Add(new ImageLike { ImageId = images[0].Id, UserId = account.Response.User.Id, CreatedAtUtc = clock.GetUtcNow() });
        await context.SaveChangesAsync();
        return (clock, account.Response.AccessToken, images[0].Id, account.Response.User.Id);
    }

    private sealed class FailCommand(string marker) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(marker)) throw new InvalidOperationException("exception-secret");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeadlineClock(TimeProvider clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != TimeSpan.FromSeconds(10) || period != Timeout.InfiniteTimeSpan)
                return System.CreateTimer(callback, state, dueTime, period);
            callback(state);
            return new NoOpTimer();
        }
        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
