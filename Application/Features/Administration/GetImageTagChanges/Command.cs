using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetImageTagChanges;

public sealed record Command(ImageTagChangeStatus? Status, int? ImageId, int? EditedByUserId, int Page = 1, int PageSize = 20)
    : IRequest<PageableImageTagChangesDto>;
