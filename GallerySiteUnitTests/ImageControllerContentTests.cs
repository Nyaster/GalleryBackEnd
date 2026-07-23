using Application.Features.Images.GetImageContent;
using GallerySiteBackend.Presentation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GallerySiteUnitTests;

public sealed class ImageControllerContentTests
{
    [Fact]
    public async Task GetContent_PreviewFormat_SendsPreviewCommandAndReturnsJpeg()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(service => service.Send(It.IsAny<Command>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageContent(new MemoryStream([1, 2, 3]), "image/jpeg"));
        var controller = Controller(mediator.Object);

        var result = await controller.GetContent(42, "preview");

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("private, max-age=3600", controller.Response.Headers.CacheControl);
        mediator.Verify(service => service.Send(It.Is<Command>(command =>
            command.Id == 42 && command.Format == ImageContentFormat.Preview), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContent_UnknownFormat_ReturnsBadRequestWithoutSendingCommand()
    {
        var mediator = new Mock<IMediator>();
        var controller = Controller(mediator.Object);

        var result = await controller.GetContent(42, "webp");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("format must be 'original', 'jpeg', or 'preview'.", badRequest.Value);
        mediator.Verify(service => service.Send(It.IsAny<Command>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ImageController Controller(IMediator mediator)
        => new(mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
