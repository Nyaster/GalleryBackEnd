using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetTags;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableTagsDto>
{
    public async Task<PageableTagsDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.AppImage.GetTagsAsync(request.Status, page, pageSize, cancellationToken);
        return new PageableTagsDto(page, pageSize, result.Total, result.Tags.Select(tag =>
            new AdminTagDto(tag.Id, tag.Name, tag.ModerationStatus, tag.CreatedAtUtc, tag.AppImages.Count)).ToArray());
    }
}
