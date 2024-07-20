using GallerySiteBackend.Context;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace GallerySiteBackend.Repositories;

public class AppUserRepository : BaseRepository<AppUser>, IAppUserRepository
{
    public AppUserRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AppUser?> GetByLoginAsync(string login)
    {
        var appUser = await _dbSet.FirstOrDefaultAsync(x => x.Login.Equals(login));
        return appUser;
    }

    public async Task<AppUser?> GetByRefreshTokenAsync(string refreshToken)
    {
        return await _dbSet.FirstOrDefaultAsync(x => x.RefreshToken.Equals(refreshToken));
    }
}