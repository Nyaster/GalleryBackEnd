namespace Entities.Exceptions;

public class InvalidLoginException(string? message) : Base400BadRequestException(message)
{
    
}