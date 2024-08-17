using Contracts;
using GallerySiteBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public class AppUserRepository : RepositoryBase<AppUser>, IAppUserRepository
{
    public AppUserRepository(RepositoryContext context) : base(context)
    {
    }

    public async Task<AppUser?> GetByLoginAsync(string login)
    {
        var appUser = await RepositoryContext.AppUsers.FirstOrDefaultAsync(x => x.Login.Equals(login));
        return appUser;
    }

    public async Task<AppUser?> GetByRefreshTokenAsync(string refreshToken)
    {
        return await RepositoryContext.AppUsers.FirstOrDefaultAsync(x => x.RefreshToken.Equals(refreshToken));
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