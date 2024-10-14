using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public class UserService : IUserService
{
    private IRepositoryManager _repositoryManager;
    private ILoggerManager _logger;
    private IMapper _mapper;

    public UserService(IRepositoryManager repositoryManager, ILoggerManager logger, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<List<AppImageDto>> GetUploadedImagesAsync(string login)
    {
        var appUser = await _repositoryManager.AppUser.GetByLoginAsync(login, trackChanges: false);
        if (appUser == null)
        {
            throw new AppUserNotFoundException("User not found");
        }

        var imagesByUser = await _repositoryManager.AppImage.GetImagesByUser(appUser.Id, trackChanges: false);
        var appImageDto = imagesByUser.Select(x => _mapper.Map<AppImageDto>(x)).ToList();
        return appImageDto;
    }
}