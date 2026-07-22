using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetImageTagChanges;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableImageTagChangesDto>
{
    public async Task<PageableImageTagChangesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.ImageTagChanges.GetAsync(request.ImageId, request.Status, request.EditedByUserId, page, pageSize, cancellationToken);
        return new PageableImageTagChangesDto(page, pageSize, result.Total, result.Changes.Select(ImageTagChangeMapper.ToDto).ToArray());
    }
}
