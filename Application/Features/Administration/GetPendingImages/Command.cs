using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetPendingImages;

public sealed record Command(int Page = 1, int PageSize = 20) : IRequest<List<AppImageDto>>;
