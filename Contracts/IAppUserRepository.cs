using GallerySiteBackend.Models;

namespace Contracts;

public interface IAppUserRepository
{
    public Task<AppUser?> GetByLoginAsync(string login, bool trackChanges);
    public Task<AppUser?> GetByRefreshTokenAsync(string refreshToken, bool trackChanges);
    void Update(AppUser user);
    Task Create(AppUser user);
}