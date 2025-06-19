using Contracts;

namespace Repository;

public class RepositoryManager(RepositoryContext repositoryContext) : IRepositoryManager
{
    private readonly Lazy<IAppImageRepository> _appImageRepository = new(() => new AppImageRepository(repositoryContext));
    private readonly Lazy<IAppUserRepository> _appUserRepository = new(() => new AppUserRepository(repositoryContext));

    public IAppUserRepository AppUser => _appUserRepository.Value;
    public IAppImageRepository AppImage => _appImageRepository.Value;

    public async Task Save()
    {
        await repositoryContext.SaveChangesAsync();
    }
}