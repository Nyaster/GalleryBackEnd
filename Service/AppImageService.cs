using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Service.Contracts;
using Shared;
using Shared.DataTransferObjects;

namespace Service;

public class AppImageService : IAppImageService
{
    private readonly IRepositoryManager _repositoryManager;
    private readonly long _fileSizeLimit = 5 * 1024 * 1024; //todo: Make this read from configuration
    private readonly ILoggerManager _logger;
    private readonly IMapper _mapper;

    private readonly List<string>
        _acceptedFileTypes = new List<string>
            { ".jpg", ".jpeg", ".png", ".webp" }; //todo:Make this read from configuration


    public AppImageService(IRepositoryManager repositoryManager, ILoggerManager logger, IMapper mapper)
    {
        _repositoryManager = repositoryManager;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<AppImageDto> UploadImageAsync(AppImageCreationDto dto, string uploadedBy)
    {
        await ValidateImage(dto.ImageFile);
        var user = await _repositoryManager.AppUser.GetByLoginAsync(uploadedBy, trackChanges: false);
        if (user == null)
        {
            throw new AppUserNotFoundException($"{uploadedBy} this user can't found, please try again");
        }

        if (dto.Tags == null)
        {
            dto = dto with { Tags = new List<string>() };
        }

        List<ImageTag> tags = await _repositoryManager.AppImage.GetTagsByNames(dto.Tags);
        _repositoryManager.AppImage.AttachTags(tags);
        var imagePath = await SaveFileToDisk(dto.ImageFile, user.Login);
        var returnedImage = Helpers.ImageHelpers.GetImageDimensions(imagePath);

        var image = new AppImage()
        {
            UploadedById = user.Id,
            UploadedDate = DateTime.Now.ToUniversalTime(),
            IsHidden = dto.IsHidden,
            Tags = tags,
            PathToFileOnDisc = imagePath,
            Width = returnedImage.width,
            Height = returnedImage.height,
        };
        await _repositoryManager.AppImage.Create(image);
        await _repositoryManager.Save();
        image.UploadedBy = user;
        var appImageDto = _mapper.Map<AppImageDto>(image);
        return appImageDto;
    }

    public async Task<FileContentResult> GetFileBytesAsync(int id)
    {
        var byId = await _repositoryManager.AppImage.GetById(id);
        var fileBytes = await System.IO.File.ReadAllBytesAsync(byId.PathToFileOnDisc);
        var contentType = GetFileType(byId.PathToFileOnDisc);
        return new FileContentResult(fileBytes, contentType);
    }

    public async Task<PageableImagesDto> GetImagesBySearchConditions(SearchImageDto getImageRequest)
    {
        List<ImageTag> list;
        if (getImageRequest.Tags == null)
        {
            list = new List<ImageTag>();
        }
        else
        {
            list = await _repositoryManager.AppImage.GetTagsByNames(getImageRequest.Tags);
        }


        var pageNumber = getImageRequest.Page;
        if (getImageRequest.Page < 1)
        {
            pageNumber = 1;
        }

        var pageSize = getImageRequest.PageSize;
        if (getImageRequest.PageSize is < 1 or > 20)
        {
            pageSize = 10;
        }

        pageNumber -= 1;
        var searchImagesByTags =
            await _repositoryManager.AppImage.SearchImagesByTags(list, OrderBy.Id, pageNumber, pageSize);
        var page = new PageableImagesDto()
        {
            Images = searchImagesByTags.listAsync.Select(x => _mapper.Map<AppImage, AppImageDto>(x)).ToList(),
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

    public string GetFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
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