using GallerySiteBackend.Models;

namespace Contracts;

public interface IAppUserRepository 
{
    public Task<AppUser?> GetByLoginAsync(string login);
    public Task<AppUser?> GetByRefreshTokenAsync(string refreshToken);
    void Update(AppUser user);
    Task Create(AppUser user);
}