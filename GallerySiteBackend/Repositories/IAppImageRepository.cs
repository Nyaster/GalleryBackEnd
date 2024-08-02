using GallerySiteBackend.Models;

namespace GallerySiteBackend.Repositories;

public interface IAppImageRepository : IBaseRepository<AppImage>
{
    public Task<List<ImageTag?>> GetTagsByIds(List<int> ids);
    public Task<List<ImageTag?>> GetTagsByNames(List<string> tags);
    public Task<List<AppImage>> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy, int page);
}