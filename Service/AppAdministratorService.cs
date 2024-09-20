using AutoMapper;
using Contracts;
using Entities.Models;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public class AppAdministratorService : IAppAdministrationService
{
    private readonly IMapper _mapper;
    private readonly IRepositoryManager _repositoryManager;

    public AppAdministratorService(IRepositoryManager repositoryManager, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _mapper = mapper;
    }


    public Task ScrapNewImagesAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<List<AppImageDto>> GetAppImagesToApproveAsync()
    {
        var notApprovedImagesAsync = await _repositoryManager.AppImage.GetNotApprovedImagesAsync();
        var appImageDtos = notApprovedImagesAsync.Select(x => _mapper.Map<AppImage, AppImageDto>(x)).ToList();
        return appImageDtos;
    }

    public Task ChangeAppImagesApprovedAsync(int imageId, bool approved)
    {
        throw new NotImplementedException();
    }
}