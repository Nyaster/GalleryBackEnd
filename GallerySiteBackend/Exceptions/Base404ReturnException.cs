namespace GallerySiteBackend.Exceptions;

public class Base404ReturnException(string? message) : Exception(message)
{
    public int ErrorCode = 404;
}