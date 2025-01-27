namespace Service.Contracts;

public interface IServiceManager
{
    IAppImageService AppImageService { get; }
    IAuthorizationService AuthorizationService { get; }
    IUserService UserService { get; }
    IAppAdministrationService AppAdministrationService { get; }
    IImageParserService AppImageParser { get; }
}