using Service.Contracts;

namespace Service;
//todo:Remove this legacy, move to original DI
public class ServiceManager(
    IAppImageService appImageService,
    IAuthorizationService authorizationService,
    IUserService userService,
    IAppAdministrationService appAdministrationService,
    IImageParserService appImageParser)
    : IServiceManager
{
    public IAppImageService AppImageService { get; } = appImageService;
    public IAuthorizationService AuthorizationService { get; } = authorizationService;
    public IUserService UserService { get; } = userService;
    public IAppAdministrationService AppAdministrationService { get; } = appAdministrationService;
    public IImageParserService AppImageParser { get; } = appImageParser;
}