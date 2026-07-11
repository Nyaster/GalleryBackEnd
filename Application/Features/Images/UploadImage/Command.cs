using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.UploadImage;

public sealed record Command(AppImageCreationDto Request) : IRequest<AppImageDto>;
