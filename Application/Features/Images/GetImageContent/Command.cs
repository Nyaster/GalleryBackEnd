using MediatR;

namespace Application.Features.Images.GetImageContent;

public sealed record Command(int Id, ImageContentFormat Format) : IRequest<ImageContent>;

public enum ImageContentFormat
{
    Original,
    Jpeg,
    Preview
}
