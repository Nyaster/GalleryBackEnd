using GallerySiteBackend.Context;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GallerySiteBackend.Repositories;

public class AppImageRepository : BaseRepository<AppImage>, IAppImageRepository
{
    private DbSet<ImageTag> _imageTags;

    public AppImageRepository(AppDbContext context) : base(context)
    {
        _imageTags = context.Set<ImageTag>();
    }

    public async Task<List<ImageTag?>> GetTagsByIds(List<int> ids)
    {
        var listAsync = await _imageTags.Where(x => ids.Contains(x.Id)).ToListAsync();
        return listAsync;
    }

    public async Task<List<ImageTag?>> GetTagsByNames(List<string> tags)
    {
        var tagsList = await _imageTags.Where(x => tags.Contains(x.Name.ToLower())).ToListAsync();
        return tagsList;
    }

    public async Task<List<AppImage>> SearchImagesByTags(List<ImageTag> tags, OrderBy orderBy, int page)
    {
        var queryable = _dbSet.AsQueryable();
        if (tags.IsNullOrEmpty())
        {
            queryable = queryable.Include(x => x.Tags)
                .Include(x => x.UploadedBy).AsQueryable();
        }
        else
        {
            queryable = queryable.Where(image => tags.All(i => image.Tags.Contains(i))).Include(x => x.Tags)
                .Include(x => x.UploadedBy).AsQueryable();
        }
        queryable = orderBy switch
        {
            OrderBy.Id => queryable.OrderBy(a => a.Id).AsQueryable(),
            OrderBy.UploadDate => queryable.OrderBy(a => a.UploadedDate).AsQueryable(),
            _ => queryable.OrderBy(a => a.Id).AsQueryable(),
        };
        var position = page * 10;
        queryable = queryable.Skip(position).Take(10).AsQueryable();
        return await queryable.ToListAsync();
    }
}

public enum OrderBy
{
    Id,
    UploadDate
}