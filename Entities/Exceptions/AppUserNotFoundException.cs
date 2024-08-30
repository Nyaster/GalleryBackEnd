namespace Entities.Exceptions;

public class AppUserNotFoundException(string user) : Base404ReturnException($"The user '{user}' was not found.")
{
}