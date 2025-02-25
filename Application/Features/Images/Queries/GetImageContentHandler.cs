using Contracts;
using Entities.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Service.Helpers;
using ImageHelpers = Application.Features.Images.Helpers.ImageHelpers;

namespace Application.Features.Images.Queries;

public class GetImageContentHandler(IRepositoryManager repository)
    : IRequestHandler<GetImageContentQuery, IActionResult>
{
    public async Task<IActionResult> Handle(GetImageContentQuery request, CancellationToken cancellationToken)
    {
        var id = request.Id;
        var asJpeg = request.AsJpeg;
        var byId = await repository.AppImage.GetById(id) ?? throw new Base404ReturnException("Image not found");
        var filePath = byId.PathToFileOnDisc;
        Stream fileBytes = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 10*1024, options:FileOptions.Asynchronous | FileOptions.SequentialScan);
        string contentType;
        
        if (asJpeg)
        {
            fileBytes = await ImageHelpers.ConvertImageToJpeg(fileBytes, cancellationToken);
            contentType = "image/jpeg";
        }
        else
        {
            contentType = GetFileType(byId.PathToFileOnDisc);
        }
        return new FileStreamResult(fileBytes, contentType);
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
}