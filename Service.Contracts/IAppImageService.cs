using Entities.Models.Requests;
using Microsoft.AspNetCore.Mvc;
using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAppImageService
{
    public Task UploadImageAsync(AppImageUploadRequest dto);
    public Task<FileContentResult> GetFileBytesAsync(int id);
    public Task<PageableImagesDTO> GetImagesBySearchConditions(GetImageRequest getImageRequest);
}