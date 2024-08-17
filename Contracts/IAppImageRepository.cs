using GallerySiteBackend.Models;

namespace Contracts;

public interface IAppImageRepository  {
    public Task<List<ImageTag?>> GetTagsByIds(List<int> ids);
    public Task<List<ImageTag?>> GetTagsByNames(List<string> tags);
    public Task<List<AppImage>> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy, int page);
    public Task Create(AppImage image);
    public Task<AppImage?> GetById(int id);
}
public enum OrderBy
{
    Id,
    UploadDate
}