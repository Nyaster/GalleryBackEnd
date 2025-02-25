using System.Net;
using Contracts;
using Entities.ErrorModel;
using Entities.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace GallerySiteBackend;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        httpContext.Response.ContentType = "application/json";
        var contextFeature = httpContext.Features.Get<IExceptionHandlerFeature>();
        if (contextFeature != null)
        {
            httpContext.Response.StatusCode = contextFeature.Error switch
            {
                AppUserNotFoundException _ => StatusCodes.Status404NotFound,
                AppUserUnauthorizedException _ => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            };
            logger.LogError($"Something went wrong: {contextFeature.Error}");
            await httpContext.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = httpContext.Response.StatusCode,
                Message = contextFeature.Error.Message
            }.ToString());
        }

        return true;
    }
}