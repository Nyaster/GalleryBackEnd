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


    public async Task<(List<AppImage> listAsync, int total)> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy,
        int page, int pageSize)
    {
        var queryable = RepositoryContext.Images.AsQueryable();
        if (tags.Count == 0)
        {
            queryable = queryable.Include(x => x.Tags)
                .Include(x => x.UploadedBy).AsQueryable();
        }
        else
        {
            queryable = queryable
                .Include(x => x.Tags)
                .Include(x => x.UploadedBy)
                .Where(image => !tags
                    .Select(t => t.Id)
                    .Except(image.Tags.Select(t => t.Id))
                    .Any());
        }

        queryable = orderBy switch
        {
            OrderBy.Id => queryable.OrderBy(a => a.Id).AsQueryable(),
            OrderBy.UploadDate => queryable.OrderBy(a => a.UploadedDate).AsQueryable(),
            _ => queryable.OrderBy(a => a.Id).AsQueryable()
        };
        var total = await queryable.CountAsync();
        var position = page * pageSize;
        queryable = queryable.Skip(position).Take(pageSize).AsQueryable();
        var listAsync = await queryable.ToListAsync();
        return (listAsync, total);
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
        var imageTags = RepositoryContext.Tags.Where(x => EF.Functions.Like(x.Name, $"%{tag.ToLower()}%")).Take(10)
            .ToList();
        return imageTags;
    }
}