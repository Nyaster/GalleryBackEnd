using System.Net;
using System.Security.Claims;
using GallerySiteBackend;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GallerySiteUnitTests;

public sealed class ApiRequestObservabilityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AuthenticatedWrite_RecordsSafeRequestAndAuditEvents()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var context = CreateContext("/api/images/42", HttpMethods.Put, "trace-42");
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/images/{id:int}"), 0,
            new EndpointMetadataCollection(), "replace-tags"));
        context.Request.RouteValues["id"] = 42;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "galleryuser"), new Claim(ClaimTypes.Role, "Admin")
        ], "test"));

        var middleware = new ApiRequestObservabilityMiddleware(
            next: current => { current.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; },
            loggerFactory, Options.Create(new ObservabilityOptions { SlowRequestMilliseconds = 1000 }));

        await middleware.InvokeAsync(context);
        await context.Features.Get<TestResponseFeature>()!.CompleteAsync();

        var request = Assert.Single(logs.Events, log => log.Category == "GallerySiteBackend.Requests");
        Assert.Equal(LogLevel.Information, request.Level);
        Assert.Equal("trace-42", request.Properties["TraceId"]);
        Assert.Equal("api/images/{id:int}", request.Properties["Route"]);
        Assert.Equal("203.0.113.10", request.Properties["ClientIp"]);

        var audit = Assert.Single(logs.Events, log => log.Category == LogCategories.Audit);
        Assert.Equal("7", audit.Properties["ActorUserId"]);
        Assert.Equal("galleryuser", audit.Properties["ActorLogin"]);
        Assert.Equal("id=42", audit.Properties["TargetRouteIds"]);
        Assert.DoesNotContain(audit.Properties.Values.OfType<string>(), value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvokeAsync_HealthRequest_DoesNotRecordAnEvent()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var context = CreateContext("/health/ready", HttpMethods.Get, "health-trace");
        var middleware = new ApiRequestObservabilityMiddleware(_ => Task.CompletedTask, loggerFactory,
            Options.Create(new ObservabilityOptions()));

        await middleware.InvokeAsync(context);
        await context.Features.Get<TestResponseFeature>()!.CompleteAsync();

        Assert.Empty(logs.Events);
    }

    private static DefaultHttpContext CreateContext(string path, string method, string traceId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.TraceIdentifier = traceId;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        var responseFeature = new TestResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Features.Set(responseFeature);
        return context;
    }

    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completed = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));

        public async Task CompleteAsync()
        {
            foreach (var (callback, state) in _completed.AsEnumerable().Reverse())
                await callback(state);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<CapturedLog> Events { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Events);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, List<CapturedLog> events) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> values
                ? values.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            events.Add(new CapturedLog(category, logLevel, properties));
        }
    }

    private sealed record CapturedLog(string Category, LogLevel Level, Dictionary<string, object?> Properties);
}
