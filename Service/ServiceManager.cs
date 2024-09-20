using AutoMapper;
using Contracts;
using Microsoft.Extensions.Configuration;
using Service.Contracts;

namespace Service;

public class ServiceManager : IServiceManager
{
    private readonly Lazy<IAppAdministrationService> _appAdministrationService;
    private readonly Lazy<IAppImageService> _appImageService;
    private readonly Lazy<IAuthorizationService> _authorizationService;

    public ServiceManager(IRepositoryManager repositoryManager, ILoggerManager logger, IConfiguration configuration,
        IMapper mapper)
    {
        _appImageService = new Lazy<IAppImageService>(() => new AppImageService(repositoryManager, logger, mapper));
        _authorizationService =
            new Lazy<IAuthorizationService>(() =>
                new AuthorizationService(repositoryManager, configuration, logger, mapper));
        _appAdministrationService =
            new Lazy<IAppAdministrationService>(() => new AppAdministratorService(repositoryManager, mapper));
    }

    public IAppImageService AppImageService => _appImageService.Value;
    public IAuthorizationService AuthorizationService => _authorizationService.Value;
    public IAppAdministrationService AppAdministrationService => _appAdministrationService.Value;
}