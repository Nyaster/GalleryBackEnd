using System.Security.Claims;
using Entities.Exceptions;
using Entities.Models;
using Service.Contracts;

namespace GallerySiteBackend;

public sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    public int? UserId => int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? Login => User?.Identity?.Name;
    public bool HasPermission(string permission) => User?.HasClaim(AccessPermission.ClaimType, permission) == true;

    public void RequireAuthenticated()
    {
        if (User?.Identity?.IsAuthenticated != true || UserId is null)
            throw new AppUserUnauthorizedException("Authentication is required.");
    }
}