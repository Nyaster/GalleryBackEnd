using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.ReplaceImageTags;

public sealed record Command(int ImageId, ReplaceImageTagsDto Request) : IRequest<ImageTagChangeDto>;
