namespace Shared.DataTransferObjects;

public record AppImageDto(
    int Id,
    string UploadedBy,
    DateTime UploadDate,
    string UrlToImage,
    string[] Tags,
    int Width,
    int Height);