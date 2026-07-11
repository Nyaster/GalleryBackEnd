using System.Net;
using Entities.ErrorModel;
using Entities.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GallerySiteBackend;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var error = httpContext.Features.Get<IExceptionHandlerFeature>()?.Error ?? exception;
        var isDuplicateLogin = error is DbUpdateException
                               {
                                   InnerException: PostgresException
                                   {
                                       SqlState: PostgresErrorCodes.UniqueViolation,
                                       ConstraintName: "UX_AppUsers_Login"
                                   }
                               };

        httpContext.Response.StatusCode = error switch
        {
            UserArleadyExistException => StatusCodes.Status409Conflict,
            _ when isDuplicateLogin => StatusCodes.Status409Conflict,
            AppUserNotFoundException => StatusCodes.Status404NotFound,
            AppUserUnauthorizedException => StatusCodes.Status401Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };
        httpContext.Response.ContentType = "application/json";
        var message = isDuplicateLogin
            ? "Username is already registered."
            : error.Message;

        logger.LogError(error, "Something went wrong while processing the request");
        await httpContext.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = httpContext.Response.StatusCode,
            Message = message
        }.ToString(), cancellationToken);

        return true;
    }
}
