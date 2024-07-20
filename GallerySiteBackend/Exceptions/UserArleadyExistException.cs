namespace GallerySiteBackend.Exceptions;

public class UserArleadyExistException(string? message) : Base409ConflictException(message)
{
    
}