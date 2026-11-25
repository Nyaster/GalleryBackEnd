using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetTags;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableTagsDto>
{
    public async Task<PageableTagsDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(AccessPermission.TagsModerate))
            throw new AppForbiddenException("Tag moderation permission is required.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.AppImage.GetTagsAsync(request.Status, page, pageSize, cancellationToken);
        return new PageableTagsDto(page, pageSize, result.Total, result.Tags);
    }
}
