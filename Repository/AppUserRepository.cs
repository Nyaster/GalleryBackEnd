using Contracts;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public class AppUserRepository(RepositoryContext context) : RepositoryBase<AppUser>(context), IAppUserRepository
{
    public async Task<AppUser?> GetByLoginAsync(string login, bool trackChanges)
    {
        var appUser = await FindByCondition(x => x.Login == login, trackChanges).SingleOrDefaultAsync();
        return appUser;
    }

    public async Task<AppUser?> GetByRefreshTokenAsync(string refreshToken, bool trackChanges)
    {
        var appUser = await FindByCondition(x => x.RefreshToken == refreshToken, trackChanges).SingleOrDefaultAsync();
        return appUser;
    }

    public new void Update(AppUser user)
    {
        base.Update(user);
    }

    public new async Task Create(AppUser user)
    {
        await base.Create(user);
    }
}