namespace Entities.Exceptions;

public class AppUserNotFoundException(string? message) : Base404ReturnException(message)
{
}