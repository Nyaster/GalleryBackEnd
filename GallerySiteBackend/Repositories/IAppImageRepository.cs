using GallerySiteBackend.Models;

namespace GallerySiteBackend.Repositories;

public interface IAppImageRepository : IBaseRepository<AppImage>
{
    public Task<List<ImageTag?>> GetTagsByIds(List<int> ids);
}