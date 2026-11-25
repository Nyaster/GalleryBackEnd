using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.ReplaceImageTags;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock)
    : IRequestHandler<Command, ImageTagChangeDto>
{
    public async Task<ImageTagChangeDto> Handle(Command request, CancellationToken cancellationToken)
    {
        // Disposal rolls back a pending transaction without masking a failed commit.
        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        var image = await repositories.AppImage.GetWithTagsByIdAsync(request.ImageId, cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureOwnerOrPermission(image.UploadedById, currentUser, AccessPermission.ImageTagsEditAny);

        if (await repositories.ImageTagChanges.GetPendingForImageAsync(image.Id, cancellationToken) is not null)
            throw new Base409ConflictException("This image already has a pending tag change.");

        var tags = await repositories.AppImage.GetOrCreateTagsAsync(request.Request.Tags ?? [], clock.GetUtcNow(),
            cancellationToken);
        if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
            throw new Base400BadRequestException("Rejected tags cannot be used.");

        var previous = ImageTagChangeMapper.Snapshot(image.Tags);
        var proposed = ImageTagChangeMapper.Snapshot(tags);
        if (ImageTagChangeMapper.SameSnapshot(previous, proposed))
            throw new Base400BadRequestException("The proposed tags are identical to the active tags.");

        var now = clock.GetUtcNow();
        var canEditAny = currentUser.HasPermission(AccessPermission.ImageTagsEditAny);
        var appliesImmediately = canEditAny ||
                                 (ImageAuthorization.IsOwner(image.UploadedById, currentUser) &&
                                  currentUser.HasPermission(AccessPermission.ImageTagsBypassReviewOnOwnImages));
        var change = new ImageTagChange
        {
            ImageId = image.Id,
            PreviousTags = previous,
            ProposedTags = proposed,
            Kind = ImageTagChangeKind.Replacement,
            Status = appliesImmediately ? ImageTagChangeStatus.Approved : ImageTagChangeStatus.Pending,
            EditedByUserId = currentUser.UserId,
            EditedByLogin = currentUser.Login ?? "unknown",
            CreatedAtUtc = now,
            AppliedAtUtc = appliesImmediately ? now : null,
            ReviewedByUserId = canEditAny ? currentUser.UserId : null,
            ReviewedByLogin = canEditAny ? currentUser.Login ?? "unknown" : null,
            ReviewedAtUtc = canEditAny ? now : null,
            ReviewNote = appliesImmediately && !canEditAny
                ? "Applied automatically with the owner's tag-change review bypass permission."
                : null
        };

        if (appliesImmediately) image.Tags = tags;
        await repositories.ImageTagChanges.AddAsync(change, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ImageTagChangeMapper.ToDto(change);
    }
}