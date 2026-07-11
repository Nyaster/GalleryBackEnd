using System.Net;
using Entities.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GallerySiteBackend;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
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
        logger.LogError(error, "Request {Method} {Path} failed with {StatusCode}", context.Request.Method, context.Request.Path, status);
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
