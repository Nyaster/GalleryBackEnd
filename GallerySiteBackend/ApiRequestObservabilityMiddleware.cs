using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace GallerySiteBackend;

public sealed class ApiRequestObservabilityMiddleware(
    RequestDelegate next,
    ILoggerFactory loggerFactory,
    IOptions<ObservabilityOptions> options)
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete
    };

    private readonly ILogger _requestLogger = loggerFactory.CreateLogger("GallerySiteBackend.Requests");
    private readonly ILogger _auditLogger = loggerFactory.CreateLogger(LogCategories.Audit);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var route = GetRouteTemplate(context);
        var method = context.Request.Method;
        var traceId = context.TraceIdentifier;
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var targetRouteIds = GetTargetRouteIds(context);
        var isWrite = MutatingMethods.Contains(method);

        context.Response.OnCompleted(() =>
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var login = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
            var level = statusCode >= StatusCodes.Status500InternalServerError
                ? LogLevel.Error
                : statusCode >= StatusCodes.Status400BadRequest || elapsedMilliseconds >= options.Value.SlowRequestMilliseconds
                    ? LogLevel.Warning
                    : LogLevel.Information;

            _requestLogger.Log(level,
                "ApiRequestCompleted {TimestampUtc} {TraceId} {Method} {Route} {StatusCode} {ElapsedMilliseconds} {UserId} {Login} {ClientIp}",
                DateTimeOffset.UtcNow, traceId, method, route, statusCode, Math.Round(elapsedMilliseconds, 2), userId, login, clientIp);

            if (isWrite)
            {
                var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
                _auditLogger.Log(statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error :
                    statusCode >= StatusCodes.Status400BadRequest ? LogLevel.Warning : LogLevel.Information,
                    "ApiWriteCompleted {TimestampUtc} {TraceId} {ActionRoute} {Method} {StatusCode} {ElapsedMilliseconds} {ActorUserId} {ActorLogin} {ActorRoles} {ClientIp} {TargetRouteIds}",
                    DateTimeOffset.UtcNow, traceId, route, method, statusCode, Math.Round(elapsedMilliseconds, 2), userId, login, roles, clientIp, targetRouteIds);
            }

            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string GetRouteTemplate(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "/"
            : context.Request.Path.Value ?? "/";

    private static string? GetTargetRouteIds(HttpContext context)
    {
        var values = context.Request.RouteValues
            .Where(value => value.Key.EndsWith("id", StringComparison.OrdinalIgnoreCase) && value.Value is not null)
            .Select(value => $"{value.Key}={value.Value}")
            .ToArray();
        return values.Length == 0 ? null : string.Join(',', values);
    }
}
