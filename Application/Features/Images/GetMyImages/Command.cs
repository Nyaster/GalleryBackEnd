using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetMyImages;

public sealed record Command(bool IncludeHidden = false, int Page = 1, int PageSize = 20) : IRequest<PageableImagesDto>;
