using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace GallerySiteBackend;

public sealed class SecurityAuthorizationMiddlewareResultHandler(ILoggerFactory loggerFactory) : IAuthorizationMiddlewareResultHandler
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(LogCategories.Security);
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged || authorizeResult.Forbidden)
        {
            _logger.LogWarning(
                "AuthorizationDenied {TimestampUtc} {TraceId} {Method} {Route} {Outcome} {ClientIp}",
                DateTimeOffset.UtcNow, context.TraceIdentifier, context.Request.Method, GetRoute(context),
                authorizeResult.Challenged ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static string GetRoute(HttpContext context)
        => context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? "/"
            : context.Request.Path.Value ?? "/";
}
