using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAppAdministrationService
{
    public Task ScrapNewImagesAsync();
    public Task<List<AppImageDto>> GetAppImagesToApproveAsync();
    public Task ChangeAppImagesApprovedAsync(int imageId, bool approved);
}