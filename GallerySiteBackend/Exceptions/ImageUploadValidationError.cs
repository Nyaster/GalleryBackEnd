namespace GallerySiteBackend.Exceptions;

public class ImageUploadValidationError(string? message) : Base400BadRequestException(message)
{
    
}