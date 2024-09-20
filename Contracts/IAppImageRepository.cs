using Entities.Models;
using GallerySiteBackend.Models;

namespace Contracts;

public interface IAppImageRepository
{
    public Task<List<ImageTag?>> GetTagsByNames(IEnumerable<string> tags);

    public Task<(List<AppImage> images, int total)> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy,
        int page, int pageSize);

    public Task Create(AppImage image);
    public Task<AppImage?> GetById(int id);
    public void AttachTags(List<ImageTag> tags);
    Task<List<ImageTag>> GetExistingTagsFromDb(List<string> tagsList);
    Task AddTags(List<ImageTag> newTags);
    Task<List<AppImage>> FindImageByMediaId(List<AppImage> images);
    Task AddImagesAsync(List<AppImage> list);
    Task<List<ImageTag>> GetTagsSuggestion(string tags);
    public Task<List<AppImage>> GetNotApprovedImagesAsync();
}

public enum OrderBy
{
    Id,
    UploadDate
}