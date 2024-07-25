using GallerySiteBackend.Exceptions;
using GallerySiteBackend.Models;
using GallerySiteBackend.Models.Requests;
using GallerySiteBackend.Repositories;
using GallerySiteBackend.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace GallerySiteUnitTests;

public class AppImageServiceTests
{
    private readonly Mock<IAppImageRepository> _appImageRepositoryMock;
    private readonly Mock<IAppUserRepository> _userRepositoryMock;
    private readonly AppImageService _appImageService;

    public AppImageServiceTests()
    {
        _appImageRepositoryMock = new Mock<IAppImageRepository>();
        _userRepositoryMock = new Mock<IAppUserRepository>();
        _appImageService = new AppImageService(_appImageRepositoryMock.Object, _userRepositoryMock.Object);
    }

    [Fact]
    public async Task UploadImageAsync_ShouldThrowException_WhenUserNotFound()
    {
        // Arrange
        var request = new AppImageUploadRequest
        {
            Image = new FormFile(new MemoryStream(), 0, 1, "Data", "test.jpg"),
            UploadedBy = "unknownUser",
            TagIds = new List<int> { 1, 2 },
            IsHidden = false
        };

        _userRepositoryMock.Setup(x => x.GetByLoginAsync(It.IsAny<string>())).ReturnsAsync((AppUser)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AppUserNotFoundException>(() => _appImageService.UploadImageAsync(request));
        Assert.Equal("unknownUser this user can't found, please try again", exception.Message);
    }

    [Fact]
    public async Task UploadImageAsync_ShouldThrowException_WhenFileSizeExceedsLimit()
    {
        // Arrange
        var largeFile = new FormFile(new MemoryStream(new byte[6 * 1024 * 1024]), 0, 6 * 1024 * 1024, "Data", "test.jpg");
        var request = new AppImageUploadRequest
        {
            Image = largeFile,
            UploadedBy = "validUser",
            TagIds = new List<int> { 1, 2 },
            IsHidden = false
        };

        _userRepositoryMock.Setup(x => x.GetByLoginAsync(It.IsAny<string>())).ReturnsAsync(new AppUser());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ImageUploadValidationError>(() => _appImageService.UploadImageAsync(request));
        Assert.Equal("File size exceeds the maximum limit of 5 MB.", exception.Message);
    }

    [Fact]
    public async Task UploadImageAsync_ShouldThrowException_WhenInvalidFileFormat()
    {
        // Arrange
        var invalidFile = new FormFile(new MemoryStream(), 0, 1, "Data", "test.txt");
        var request = new AppImageUploadRequest
        {
            Image = invalidFile,
            UploadedBy = "validUser",
            TagIds = new List<int> { 1, 2 },
            IsHidden = false
        };

        _userRepositoryMock.Setup(x => x.GetByLoginAsync(It.IsAny<string>())).ReturnsAsync(new AppUser());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ImageUploadValidationError>(() => _appImageService.UploadImageAsync(request));
        Assert.Equal("Invalid file format. Only .jpg, .jpeg, .png, and .gif are allowed.", exception.Message);
    }

    [Fact]
    public async Task UploadImageAsync_ShouldSaveImage_WhenValidRequest()
    {
        // Arrange
        var validFile = new FormFile(new MemoryStream(new byte[1024]), 0, 1024, "Data", "test.jpg");
        var request = new AppImageUploadRequest
        {
            Image = validFile,
            UploadedBy = "validUser",
            TagIds = new List<int> { 1, 2 },
            IsHidden = false
        };

        var user = new AppUser { Login = "validUser" };
        _userRepositoryMock.Setup(x => x.GetByLoginAsync(It.IsAny<string>())).ReturnsAsync(user);
        _appImageRepositoryMock.Setup(x => x.GetTagsByIds(It.IsAny<List<int>>())).ReturnsAsync(new List<ImageTag>());
        _appImageRepositoryMock.Setup(x => x.Add(It.IsAny<AppImage>())).Returns(Task.CompletedTask);

        // Act
        await _appImageService.UploadImageAsync(request);

        // Assert
        _appImageRepositoryMock.Verify(x => x.Add(It.IsAny<AppImage>()), Times.Once);
    }
}