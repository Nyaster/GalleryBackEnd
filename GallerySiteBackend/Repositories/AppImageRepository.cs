using GallerySiteBackend.Context;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

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
}