﻿using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Service.Contracts;
using Service.Helpers;
using Shared.DataTransferObjects;

namespace Service;

public class AppImageService : IAppImageService
{
    private readonly List<string>
        _acceptedFileTypes = [".jpg", ".jpeg", ".png", ".webp"]; //todo:Make this read from configuration

    private readonly long _fileSizeLimit = 20 * 1024 * 1024; //todo: Make this read from configuration
    private readonly ILogger<AppImageService> _logger;
    private readonly IMapper _mapper;
    private readonly IRepositoryManager _repositoryManager;


    public AppImageService(IRepositoryManager repositoryManager, ILogger<AppImageService> logger, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<AppImageDto> UploadImageAsync(AppImageCreationDto dto, string uploadedBy)
    {
        await ValidateImage(dto.ImageFile);
        var user = await _repositoryManager.AppUser.GetByLoginAsync(uploadedBy, false);
        if (user == null) throw new AppUserNotFoundException($"{uploadedBy} this user can't found, please try again");

        if (dto.Tags == null) dto = dto with { Tags = new List<string>() };

        List<ImageTag?> tags = await _repositoryManager.AppImage.GetTagsByNames(dto.Tags);
        _repositoryManager.AppImage.AttachTags(tags);
        var imagePath = await SaveFileToDisk(dto.ImageFile, user.Login);
        var returnedImage = await ImageHelpers.GetImageDimensionsAsync(imagePath);

        var image = new UserMadeImage()
        {
            UploadedById = user.Id,
            UploadedDate = DateTime.Now.ToUniversalTime(),
            IsHidden = dto.IsHidden,
            Tags = tags,
            PathToFileOnDisc = imagePath,
            Width = returnedImage.Width,
            Height = returnedImage.Height
        };
        await _repositoryManager.AppImage.Create(image);
        await _repositoryManager.Save();
        image.UploadedBy = user;
        var appImageDto = _mapper.Map<AppImageDto>(image);
        return appImageDto;
    }

    public async Task<FileContentResult> GetFileBytesAsync(int id, bool asJpeg) //realizaed
    {
        var byId = await _repositoryManager.AppImage.GetById(id);
        var filePath = byId.PathToFileOnDisc;
        byte[] fileBytes;
        string contentType;
        if (asJpeg)
        {
            fileBytes = await ImageHelpers.ConvertImageToJpeg(filePath);
            contentType = "image/jpeg";
        }
        else
        {
            fileBytes = await File.ReadAllBytesAsync(byId.PathToFileOnDisc);
            contentType = GetFileType(byId.PathToFileOnDisc);
        }

        return new FileContentResult(fileBytes, contentType);
    }

    private string GetFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    public async Task<PageableImagesDto> GetImagesBySearchConditions(SearchImageDto getImageRequest)
    {
        List<ImageTag> list;
        if (getImageRequest.Tags == null)
            list = new List<ImageTag>();
        else
            list = await _repositoryManager.AppImage.GetTagsByNames(getImageRequest.Tags);


        var pageNumber = getImageRequest.Page;
        if (getImageRequest.Page < 1) pageNumber = 1;

        var pageSize = getImageRequest.PageSize;
        if (getImageRequest.PageSize is < 1 or > 20) pageSize = 10;

        pageNumber -= 1;
        var searchImagesByTags =
            await _repositoryManager.AppImage.SearchImagesByTags(list, OrderBy.Id, pageNumber, pageSize ,false);
        var page = new PageableImagesDto
        {
            Images = searchImagesByTags.images.Select(x => _mapper.Map<AppImage, AppImageDto>(x)).ToList(),
            OrderBy = OrderBy.Id.ToString(),
            Page = getImageRequest.Page,
            PageSize = pageSize,
            Total = searchImagesByTags.total
        };
        return page;
    }

    public async Task<List<TagsDto>> GetTagsSuggestion(string tag)
    {
        var tagsSuggestion = await _repositoryManager.AppImage.GetTagsSuggestion(tag);
        var tagsDtos = tagsSuggestion.Select(x => new TagsDto(x.Name)).ToList();
        return tagsDtos;
    }

    public async Task<AppImageDto> GetImageByIdAsync(int id)
    {
        var byId = await _repositoryManager.AppImage.GetById(id);
        var appImageDto = _mapper.Map<AppImage, AppImageDto>(byId);
        return appImageDto;
    }


    private async Task ValidateImage(IFormFile file)
    {
        //todo:Checks File signatures before saving image;
        if (file.Length > _fileSizeLimit)
        {
            var validationError = "File size exceeds the maximum limit of 5 MB.";
            throw new ImageUploadValidationError(validationError);
        }
        var allowedSignatures = new Dictionary<string, byte[]>
        {
            { ".jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 } },
            { ".png", new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
            { ".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 } } // "RIFF"
        };
        await using var stream = file.OpenReadStream();
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, 0, 4);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedSignatures.ContainsKey(extension) || !header.SequenceEqual(allowedSignatures[extension]))
        {
            throw new ArgumentException("Invalid file type.");
        }

    }

    private async Task<string> SaveFileToDisk(IFormFile file, string userLogin)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var randomFileName = $"{Guid.NewGuid()}{extension}";
        var safeSubfolder =
            Path.GetInvalidFileNameChars().Aggregate(userLogin, (current, c) => current.Replace(c, '_'));

        var uploadsFolder = Path.Combine("upload", "images", safeSubfolder);
        Directory.CreateDirectory(uploadsFolder);
        var filePath = Path.Combine(uploadsFolder, randomFileName);
        await using var filestream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(filestream);
        return filePath;
    }
}