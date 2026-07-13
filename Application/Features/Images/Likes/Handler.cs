using Application.Features.Images.Comments;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Likes;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<SetLikeCommand, LikeSummaryDto>
{
    public async Task<LikeSummaryDto> Handle(SetLikeCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, false, cancellationToken) ?? throw new Base404ReturnException("Image not found.");
        GetCommentsHandler.EnsurePublic(image);
        var userId = currentUser.UserId!.Value;
        await repositories.Interactions.SetLikeAsync(image.Id, userId, request.IsLiked, clock.GetUtcNow(), cancellationToken);
        return new LikeSummaryDto(await repositories.Interactions.GetLikeCountAsync(image.Id, cancellationToken), request.IsLiked);
    }
}
