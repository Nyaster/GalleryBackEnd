using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Likes;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock)
    : IRequestHandler<SetLikeCommand, LikeSummaryDto>
{
    public async Task<LikeSummaryDto> Handle(SetLikeCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var image = await ImageAuthorization.GetPublicMetadataAsync(repositories.AppImage, request.ImageId,
            currentUser, cancellationToken);
        var userId = currentUser.UserId!.Value;
        await repositories.Interactions.SetLikeAsync(image.Id, userId, request.IsLiked, clock.GetUtcNow(),
            cancellationToken);
        return new LikeSummaryDto(await repositories.Interactions.GetLikeCountAsync(image.Id, cancellationToken),
            request.IsLiked);
    }
}
