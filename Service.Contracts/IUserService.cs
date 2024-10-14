using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IUserService
{
    public Task<List<AppImageDto>> GetUploadedImagesAsync(string login);
}