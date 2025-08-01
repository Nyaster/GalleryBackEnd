﻿using System.Linq.Expressions;
using Entities.Models;
using GallerySiteBackend.Models;

namespace Contracts;

public interface IAppImageRepository : IRepositoryBase<AppImage>
{
    public Task<List<ImageTag?>> GetTagsByNames(IEnumerable<string> tags);

    public Task<(List<AppImage> images, int total)> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy,
        int page, int pageSize,bool fanImages);

    public Task Create(AppImage image);
    public Task<AppImage?> GetById(int id);
    public void AttachTags(List<ImageTag> tags);
    Task<List<ImageTag>> GetExistingTagsFromDb(List<string> tagsList);
    Task AddTags(List<ImageTag> newTags);
    Task<List<AppImage>> FindImageByMediaId(List<AppImage?> images, bool trackChanges);
    Task AddImagesAsync(List<AppImage> list);
    Task<List<ImageTag>> GetTagsSuggestion(string tags);
    public Task<List<AppImage>> GetNotApprovedImagesAsync();
    public void AttachImages(List<AppImage> images);
    public void UpdateImages(List<AppImage> images);
    public Task<List<AppImage>> GetImagesByUser(int userId, bool trackChanges);
    public Task<IQueryable<AppImage>> FindImageByCondition(Expression<Func<AppImage, bool>> expression, bool trackChanges);
 }

public enum OrderBy
{
    Id,
    UploadDate
}