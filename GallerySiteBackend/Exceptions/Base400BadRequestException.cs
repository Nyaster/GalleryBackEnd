namespace GallerySiteBackend.Exceptions;

public class Base400BadRequestException(string? message) : Exception(message)
{
    public int ErrorCode = 400;
}