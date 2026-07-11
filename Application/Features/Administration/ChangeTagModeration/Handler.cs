using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeTagModeration;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, AdminTagDto>
{
    public async Task<AdminTagDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        if (request.Status is not (TagModerationStatus.Approved or TagModerationStatus.Rejected))
            throw new Base400BadRequestException("Tag moderation status must be Approved or Rejected.");
        var tag = await repositories.AppImage.GetTagByIdAsync(request.TagId, true, cancellationToken)
            ?? throw new Base404ReturnException("Tag not found.");
        tag.ModerationStatus = request.Status;
        if (request.Status == TagModerationStatus.Rejected)
            tag.AppImages.Clear();
        await repositories.SaveAsync(cancellationToken);
        return new AdminTagDto(tag.Id, tag.Name, tag.ModerationStatus, tag.CreatedAtUtc, tag.AppImages.Count);
    }
}
