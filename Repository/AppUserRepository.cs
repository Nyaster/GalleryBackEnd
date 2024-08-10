using Contracts;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public class AppUserRepository : BaseRepository<AppUser>, IAppUserRepository
{
    public AppUserRepository(RepositoryContext context) : base(context)
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