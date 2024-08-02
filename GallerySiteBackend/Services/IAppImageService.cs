using System.Net;
using GallerySiteBackend.Models.DTOs;
using GallerySiteBackend.Models.Requests;
using Microsoft.AspNetCore.Mvc;

namespace GallerySiteBackend.Services;

public interface IAppImageService
{
    public Task UploadImageAsync(AppImageUploadRequest dto);
    public Task<FileContentResult> GetFileBytesAsync(int id);
    public Task<PageableImagesDTO> GetImagesBySearchConditions(GetImageRequest getImageRequest);
}