using GallerySiteBackend.Models;

namespace GallerySiteBackend.Repositories;

public interface IAppUserRepository : IBaseRepository<AppUser>
{
    public Task<AppUser?> GetByLoginAsync(string login);
    public Task<AppUser?> GetByRefreshTokenAsync(string refreshToken);
}