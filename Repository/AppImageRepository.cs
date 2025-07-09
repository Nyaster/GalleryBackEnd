using Contracts;
using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public class AppImageRepository(RepositoryContext repositoryContext)
    : RepositoryBase<AppImage>(repositoryContext), IAppImageRepository
{
    public async Task<List<ImageTag?>> GetTagsByNames(IEnumerable<string> tags)
    {
        var listAsync = await RepositoryContext.Tags.AsNoTracking().Where(x => tags.Contains(x.Name.ToLower()))
            .ToListAsync();

        return listAsync;
    }

    public async Task<(List<AppImage> images, int total)> SearchImagesByTags(
        List<ImageTag> tags,
        OrderBy orderBy,
        int page,
        int pageSize,
        bool fanImages)
    {
        if (fanImages)
        {
            var queryable = RepositoryContext.UserMadeImages.AsQueryable();
            return await SearchInternal<UserMadeImage>(queryable, tags, orderBy, page, pageSize);
        }
        else
        {
            var queryable = RepositoryContext.SelebusImages.AsQueryable();
            return await SearchInternal<SelebusImage>(queryable, tags, orderBy, page, pageSize);
        }
    }

    private async Task<(List<AppImage> images, int total)> SearchInternal<T>(
        IQueryable<T> queryable,
        List<ImageTag> tags,
        OrderBy orderBy,
        int page,
        int pageSize) where T : AppImage
    {
        queryable = (tags.Count == 0
            ? IncludeStandardProperties(queryable)
            : IncludeAndFilterByTags(queryable, tags));

        queryable = ApplyOrdering(queryable, orderBy);

        var total = await queryable.CountAsync();
        var skip = page * pageSize;
        var images = await queryable.Skip(skip).Take(pageSize).ToListAsync();

        // Upcast to AppImage if needed for return type
        return (images.Cast<AppImage>().ToList(), total);
    }


    public new async Task Create(AppImage image)
    {
        await base.Create(image);
    }

    public async Task<AppImage?> GetById(int id)
    {
        return await RepositoryContext.Images.Include(x => x.Tags).FirstOrDefaultAsync(x => x.Id == id);
    }

    public void AttachTags(List<ImageTag> tags)
    {
        RepositoryContext.Tags.AttachRange(tags);
    }

    public async Task<List<ImageTag>> GetExistingTagsFromDb(List<string> tagsList)
    {
        return await RepositoryContext.Tags.Where(x => tagsList.Contains(x.Name)).ToListAsync();
    }

    public async Task AddTags(List<ImageTag> newTags)
    {
        await repositoryContext.Tags.AddRangeAsync(newTags);
    }

    public async Task<List<AppImage>> FindImageByMediaId(List<AppImage?> images, bool trackChanges)
    {
        var enumerable = images.Select(x => x.MediaId).ToList();
        return await FindByCondition(x => enumerable.Contains(x.MediaId), trackChanges).Include(x => x.Tags)
            .ToListAsync();
    }

    public async Task AddImagesAsync(List<AppImage> appImages)
    {
        await RepositoryContext.Images.AddRangeAsync(appImages);
    }

    public async Task<List<ImageTag>> GetTagsSuggestion(string tag)
    {
        var imageTags = await RepositoryContext.Tags.Where(x => EF.Functions.Like(x.Name, $"%{tag.ToLower()}%"))
            .Take(50)
            .ToListAsync();
        return imageTags;
    }

    public async Task<List<AppImage>> GetNotApprovedImagesAsync()
    {
        var notApprovedImages = await FindByCondition(x => x.IsHidden == true, false).ToListAsync();
        return notApprovedImages;
    }

    public void AttachImages(List<AppImage> images)
    {
        RepositoryContext.Images.AttachRange(images);
    }

    public void UpdateImages(List<AppImage> images)
    {
        RepositoryContext.Images.UpdateRange(images);
    }

    public async Task<List<AppImage>> GetImagesByUser(int userId, bool trackChanges)
    {
        return await FindByCondition(x => x.UploadedById == userId, trackChanges)
            .Include(x => x.Tags).Include(x => x.UploadedBy).ToListAsync();
    }

    private IQueryable<T> IncludeStandardProperties<T>(IQueryable<T> queryable) where T : AppImage
     {
        return queryable.Include(x => x.Tags)
            .Include(x => x.UploadedBy)
            .AsQueryable();
    }

    private IQueryable<T> IncludeAndFilterByTags<T>(IQueryable<T> queryable, List<ImageTag> tags) where T : AppImage 
    {
        var tagIds = tags.Select(t => t.Id).ToList();

        return queryable.Include(image => image.Tags)
            .Include(image => image.UploadedBy)
            .Where(image => tagIds.All(tagId => image.Tags.Any(tag => tag.Id == tagId)));
    }

    private IQueryable<T> ApplyOrdering<T>(IQueryable<T> queryable, OrderBy orderBy) where T : AppImage
    {
        return orderBy switch
        {
            OrderBy.Id => queryable.OrderByDescending(a => a.MediaId).ThenBy(x => x.UploadedDate).ThenBy(x => x.Id)
                .AsQueryable(),
            OrderBy.UploadDate => queryable.OrderByDescending(a => a.UploadedDate).ThenBy(a => a.MediaId).ThenBy(x => x.Id)
                .AsQueryable(),
            _ => queryable.OrderBy(a => a.MediaId).ThenBy(x => x.UploadedDate).AsQueryable()
        };
    }
}