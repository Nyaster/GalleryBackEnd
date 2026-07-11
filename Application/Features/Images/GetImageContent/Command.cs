using MediatR;

namespace Application.Features.Images.GetImageContent;

public sealed record Command(int Id, bool AsJpeg) : IRequest<ImageContent>;
