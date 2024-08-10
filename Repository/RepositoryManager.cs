﻿using Contracts;

namespace Repository;

public class RepositoryManager : IRepositoryManager
{
    private readonly RepositoryContext _repositoryContext;
    private readonly Lazy<IAppImageRepository> _appImageRepository;
    private readonly Lazy<IAppUserRepository> _appUserRepository;

    public RepositoryManager(Lazy<IAppUserRepository> appUserRepository, Lazy<IAppImageRepository> appImageRepository,
        RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
        _appUserRepository = new Lazy<IAppUserRepository>(() => new AppUserRepository(repositoryContext));
        _appImageRepository = new Lazy<IAppImageRepository>(() => new AppImageRepository(repositoryContext));
    }

    public IAppUserRepository AppUser => _appUserRepository.Value;
    public IAppImageRepository AppImage => _appImageRepository.Value;

    public void Save()
    {
        _repositoryContext.SaveChanges();
    }
}