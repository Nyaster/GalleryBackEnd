using System.Net;
using Entities.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GallerySiteBackend;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, ILoggerFactory loggerFactory) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error ?? exception;
        var status = error switch
        {
            Base400BadRequestException => StatusCodes.Status400BadRequest,
            Base401UnauthorizedException => StatusCodes.Status401Unauthorized,
            AppForbiddenException => StatusCodes.Status403Forbidden,
            Base404ReturnException => StatusCodes.Status404NotFound,
            Base409ConflictException => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var route = context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? context.Request.Path.Value
            : context.Request.Path.Value;
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(error, "Request {Method} {Route} failed with {StatusCode} {TraceId}", context.Request.Method, route, status, context.TraceIdentifier);
        else
            logger.LogWarning(error, "Request {Method} {Route} failed with {StatusCode} {TraceId}", context.Request.Method, route, status, context.TraceIdentifier);
        if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            loggerFactory.CreateLogger(LogCategories.Security).LogWarning(
                "AuthenticationOrAuthorizationFailed {TimestampUtc} {TraceId} {Method} {Route} {StatusCode} {ClientIp}",
                DateTimeOffset.UtcNow, context.TraceIdentifier, context.Request.Method,
                context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint ? routeEndpoint.RoutePattern.RawText ?? context.Request.Path.Value : context.Request.Path.Value,
                status, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
        var detail = status == StatusCodes.Status500InternalServerError
            ? "An unexpected server error occurred."
            : error.Message;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
