namespace Entities.Exceptions;

public class Base409ConflictException(string? message) : Exception(message)
{
    public int ErrorCode = 409;
}