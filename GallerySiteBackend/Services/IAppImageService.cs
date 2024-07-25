using GallerySiteBackend.Models.Requests;

namespace GallerySiteBackend.Services;

public interface IAppImageService
{
    public Task UploadImageAsync(AppImageUploadRequest dto);
}