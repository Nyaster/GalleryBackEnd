namespace GallerySiteBackend.Exceptions;

public class AppUserNotFoundException(string? message) : Base404ReturnException(message)
{
}