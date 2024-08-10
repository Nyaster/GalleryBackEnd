using System.Net;
using Contracts;
using Entities.Models.Requests;
using GallerySiteBackend.Exceptions;
using GallerySiteBackend.Models;
using GallerySiteBackend.Models.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace GallerySiteBackend.Services;

public class AppImageService : IAppImageService
{
    private readonly IAppImageRepository _appImageRepository;
    private readonly IAppUserRepository _userRepository;
    private readonly long _fileSizeLimit = 5 * 1024 * 1024; //todo: Make this read from configuration

    private readonly List<string>
        _acceptedFileTypes = new List<string> { ".jpg", ".jpeg", ".png" }; //todo:Make this read from configuration

    public AppImageService(IAppImageRepository appImageRepository, IAppUserRepository userRepository)
    {
        _appImageRepository = appImageRepository;
        _userRepository = userRepository;
    }

    public async Task UploadImageAsync(AppImageUploadRequest dto)
    {
        await ValidateImage(dto.Image);
        var user = await _userRepository.GetByLoginAsync(dto.UploadedBy);
        if (user == null)
        {
            throw new AppUserNotFoundException($"{dto.UploadedBy} this user can't found, please try again");
        }

        var tags = await _appImageRepository.GetTagsByIds(dto.TagIds);
        var imagePath = await SaveFileToDisk(dto.Image, user.Login);
        var image = new AppImage()
        {
            UploadedBy = user,
            UploadedDate = DateTime.Now.ToUniversalTime(),
            IsHidden = dto.IsHidden,
            Tags = tags,
            PathToFileOnDisc = imagePath,
        };
        await _appImageRepository.Add(image);
    }

    public async Task<FileContentResult> GetFileBytesAsync(int id)
    {
        var byId = await _appImageRepository.GetById(id);
        var fileBytes = await System.IO.File.ReadAllBytesAsync(byId.PathToFileOnDisc);
        var contentType = GetFileType(byId.PathToFileOnDisc);
        return new FileContentResult(fileBytes, contentType);
    }

    public async Task<PageableImagesDTO> GetImagesBySearchConditions(GetImageRequest getImageRequest)
    {
        List<ImageTag> list = new List<ImageTag>();
        if (getImageRequest.Page < 1)
        {
            getImageRequest.Page = 1;
        }

        var pageNumber = getImageRequest.Page - 1;
        var searchImagesByTags = await _appImageRepository.SearchImagesByTags(list, OrderBy.Id, pageNumber);
        var page = new PageableImagesDTO()
        {
            Images = searchImagesByTags.Select(x => new AppImageDTO()
            {
                Id = x.Id,
                UploadedBy = x.UploadedBy.Login,
                UploadDate = x.UploadedDate,
                UrlToImage = $"/api/images/{x.Id}"
            }).ToList(),
            OrderBy = OrderBy.Id.ToString(),
            Page = getImageRequest.Page,
            PageSize = 10,
        };
        return page;
    }

    public string GetFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp"=>"image/webp",
            _ => "application/octet-stream",
        };
    }

    public Task AddTagsToImage()
    {
        throw new NotImplementedException();
    }

    private async Task ValidateImage(IFormFile file)
    {
        //todo:Checks File signatures before saving image;
        if (file.Length > _fileSizeLimit)
        {
            var validationError = "File size exceeds the maximum limit of 5 MB.";
            throw new ImageUploadValidationError(validationError);
        }

        // Check file format
        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_acceptedFileTypes.Contains(fileExtension))
        {
            var validationError = "Invalid file format. Only .jpg, .jpeg, .png, and .gif are allowed.";
            throw new ImageUploadValidationError(validationError);
        }
    }

    private async Task<string> SaveFileToDisk(IFormFile file, string userLogin)
    {
        var uploadsFolder = Path.Combine("upload", "images", userLogin);
        Directory.CreateDirectory(uploadsFolder);
        var filePath = Path.Combine(uploadsFolder, Guid.NewGuid().ToString() + Path.GetExtension(file.FileName));
        await using var filestream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(filestream);

        return filePath;
    }
}