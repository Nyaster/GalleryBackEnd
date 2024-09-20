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


    public async Task<(List<AppImage> images, int total)> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy,
        int page, int pageSize)
    {
        IQueryable<AppImage> queryable = RepositoryContext.Images.AsQueryable();

        queryable = tags.Count == 0
            ? IncludeStandardProperties(queryable)
            : IncludeAndFilterByTags(queryable, tags);

        queryable = ApplyOrdering(queryable, orderBy);

        var total = await queryable.CountAsync();
        var skip = page * pageSize;
        var images = await queryable.Skip(skip).Take(pageSize).ToListAsync();

        return (images, total);
    }

    private IQueryable<AppImage> IncludeStandardProperties(IQueryable<AppImage> queryable)
    {
        return queryable.Include(x => x.Tags)
            .Include(x => x.UploadedBy)
            .AsQueryable();
    }

    private IQueryable<AppImage> IncludeAndFilterByTags(IQueryable<AppImage> queryable, List<ImageTag> tags)
    {
        var tagIds = tags.Select(t => t.Id).ToList();

        return queryable.Include(image => image.Tags)
            .Include(image => image.UploadedBy)
            .Where(image => tagIds.All(tagId => image.Tags.Any(tag => tag.Id == tagId)));
    }

    private IQueryable<AppImage> ApplyOrdering(IQueryable<AppImage> queryable, OrderBy orderBy)
    {
        return orderBy switch
        {
            OrderBy.Id => queryable.OrderBy(a => a.Id).AsQueryable(),
            OrderBy.UploadDate => queryable.OrderBy(a => a.UploadedDate).AsQueryable(),
            _ => queryable.OrderBy(a => a.Id).AsQueryable()
        };
    }

    public new async Task Create(AppImage image) => await base.Create(image);

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

    public async Task<List<AppImage>> FindImageByMediaId(List<AppImage> images)
    {
        var enumerable = images.Select(x => x.MediaId).ToList();
        return await RepositoryContext.Images
            .Where(x => enumerable.Contains(x.MediaId))
            .ToListAsync();
    }

    public async Task AddImagesAsync(List<AppImage> appImages)
    {
        await RepositoryContext.Images.AddRangeAsync(appImages);
    }

    public async Task<List<ImageTag>> GetTagsSuggestion(string tag)
    {
        var imageTags = await RepositoryContext.Tags.Where(x => EF.Functions.Like(x.Name, $"%{tag.ToLower()}%"))
            .Take(10)
            .ToListAsync();
        return imageTags;
    }

    public async Task<List<AppImage>> GetNotApprovedImagesAsync()
    {
        var notApprovedImages = await FindByCondition(x => x.IsHidden == true, false).ToListAsync();
        return notApprovedImages;
    }
}