namespace GallerySiteBackend;

public static class ApiRequestCorrelation
{
    public static string? Operation(HttpRequest request)
        => HttpMethods.IsGet(request.Method) && string.Equals(request.Path.Value?.TrimEnd('/'),
            "/api/images/for-me", StringComparison.OrdinalIgnoreCase)
            ? request.Query.ContainsKey("cursor") ? "ReadFeedPage" : "CreateFeed"
            : null;

    public static IReadOnlyList<KeyValuePair<string, object?>> Properties(string requestId, string? operation)
    {
        var properties = new List<KeyValuePair<string, object?>> { new("RequestId", requestId) };
        if (operation is not null) properties.Add(new("Operation", operation));
        return properties;
    }
}

public sealed class ApiRequestCorrelationMiddleware(RequestDelegate next, ILogger<ApiRequestCorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }
        var requestId = context.TraceIdentifier;
        using var scope = logger.BeginScope(ApiRequestCorrelation.Properties(requestId,
            ApiRequestCorrelation.Operation(context.Request)));
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });
        await next(context);
    }
}
