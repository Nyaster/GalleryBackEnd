using Entities.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace GallerySiteBackend;

public sealed class GuestBrowsingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != "guest-read")
        {
            await next(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.Vary.ToString().Split(',')
                    .Any(value => value.Trim().Equals(HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase)))
                context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Authorization);
            if (context.User.Identity?.IsAuthenticated != true)
                context.Response.Headers.CacheControl = "private, no-store";
            return Task.CompletedTask;
        });

        // AllowAnonymous bypasses authorization even when JWT validation failed.
        // A supplied credential must therefore be checked before treating a request as a guest.
        if (context.Request.Headers.ContainsKey(HeaderNames.Authorization))
        {
            var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!result.Succeeded)
                throw new AppUserUnauthorizedException("The supplied access token is invalid or expired.");
        }

        await next(context);
    }
}
