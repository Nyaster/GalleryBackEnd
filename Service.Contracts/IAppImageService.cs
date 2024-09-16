using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.DataTransferObjects;

namespace Service.Contracts;

public interface IAppImageService
{
    public Task<AppImageDto> UploadImageAsync(AppImageCreationDto dto, string uploadedBy);
    public Task<FileContentResult> GetFileBytesAsync(int id, bool asJpeg);
    public Task<PageableImagesDto> GetImagesBySearchConditions(SearchImageDto getImageRequest);
    Task<List<TagsDto>> GetTagsSuggestion(string tags);
}