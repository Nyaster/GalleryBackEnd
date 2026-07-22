using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageTagChanges;

public sealed record Command(int ImageId, int Page = 1, int PageSize = 20) : IRequest<PageableImageTagChangesDto>;
