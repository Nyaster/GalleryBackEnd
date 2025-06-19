using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Microsoft.Extensions.Logging;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public class UserService(IRepositoryManager repositoryManager, ILogger<UserService> logger, IMapper mapper)
    : IUserService
{
    private ILogger<UserService> _logger = logger;

    public async Task<List<AppImageDto>> GetUploadedImagesAsync(string login)
    {
        var appUser = await repositoryManager.AppUser.GetByLoginAsync(login, trackChanges: false);
        if (appUser == null)
        {
            throw new AppUserNotFoundException("User not found");
        }

        var imagesByUser = await repositoryManager.AppImage.GetImagesByUser(appUser.Id, trackChanges: false);
        var appImageDto = imagesByUser.Select(x => mapper.Map<AppImageDto>(x)).ToList();
        return appImageDto;
    }
}