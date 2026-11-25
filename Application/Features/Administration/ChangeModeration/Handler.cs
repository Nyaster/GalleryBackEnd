using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeModeration;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser)
    : IRequestHandler<Command, AppImageDto?>
{
    public async Task<AppImageDto?> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var canModerate = currentUser.HasPermission(AccessPermission.ImagesModerate);
        var canClassify = currentUser.HasPermission(AccessPermission.ImagesClassifyAi);
        if ((!canModerate && !canClassify) || (request.Status is not null && !canModerate) ||
            (request.AiUsage is not null && !canClassify))
            throw new AppForbiddenException("You do not have permission to change the supplied moderation fields.");
        if (request.Status is null && request.AiUsage is null)
            throw new Base400BadRequestException("Supply a moderation status or an AI classification.");
        if (request.Status is { } status)
        {
            if (!Enum.IsDefined(status))
                throw new Base400BadRequestException("Invalid moderation status.");
            if (request.ExpectedStatus is not { } expected || !Enum.IsDefined(expected))
                throw new Base400BadRequestException("Supply a valid expectedStatus with a moderation status.");
        }
        if (request.AiUsage is { } aiUsage &&
            (aiUsage == AiUsageClassification.Unknown || !Enum.IsDefined(aiUsage)))
            throw new Base400BadRequestException("aiUsage must be HumanMade, AiAssisted, or AiGenerated.");

        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        var image = await repositories.AppImage.GetByIdAsync(request.ImageId, true, cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        if (image.DeletedAtUtc is not null) throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadableForManagement(image, currentUser);
        if (request.Status is not null && image.ModerationStatus != request.ExpectedStatus)
            throw new Base409ConflictException("The moderation status has changed. Refresh the image and retry if appropriate.");
        if (request.Status is not null)
            image.ModerationStatus = request.Status.Value;
        if (request.AiUsage is not null)
            image.AiUsage = request.AiUsage.Value;
        await repositories.SaveAsync(cancellationToken);
        var response = await ImageAuthorization.GetManagementMutationResponseAsync(repositories.AppImage, image,
            currentUser, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
