namespace Entities.Exceptions;

public sealed class AppForbiddenException(string message) : Exception(message)
{
}
