namespace GallerySiteBackend.Exceptions;

public class AppUserUnauthorizedException(string? message) : Base401UnauthorizedException(message)
{
}