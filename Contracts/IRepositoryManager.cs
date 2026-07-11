namespace Contracts;

public interface IRepositoryManager
{
    IAppUserRepository AppUser { get; }
    IAppImageRepository AppImage { get; }
    Task Save();
}