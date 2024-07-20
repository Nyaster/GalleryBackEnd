namespace GallerySiteBackend.Exceptions;

public class Base401UnauthorizedException(string? message) : Exception(message)
{
    public int ErrorCode = 401;
}