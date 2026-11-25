using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ModerateImageTagChange;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock)
    : IRequestHandler<Command, ImageTagChangeDto>
{
    public async Task<ImageTagChangeDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(AccessPermission.ImageTagsReview))
            throw new AppForbiddenException("Image-tag review permission is required.");
        // Disposal rolls back a pending transaction without masking a failed commit.
        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        var change = await repositories.ImageTagChanges.GetByIdAsync(request.ChangeId, true, cancellationToken)
                     ?? throw new Base404ReturnException("Tag change not found.");

        if (change.Kind != ImageTagChangeKind.Replacement)
            throw new Base409ConflictException("Only image tag replacements can be moderated.");
        var requiredStatus = request.Decision switch
        {
            TagChangeDecision.Approve or TagChangeDecision.Reject => ImageTagChangeStatus.Pending,
            TagChangeDecision.Revert => ImageTagChangeStatus.Approved,
            _ => throw new Base400BadRequestException("Unknown image tag change decision.")
        };
        if (change.Status != requiredStatus)
            throw new Base409ConflictException("This decision cannot be applied to the current tag change status.");

        var image = await repositories.AppImage.GetWithTagsByIdAsync(change.ImageId, cancellationToken)
                    ?? throw new Base404ReturnException("Image not found.");
        var now = clock.GetUtcNow();
        var note = ImageTagChangeMapper.NormalizeNote(request.Note);
        var reviewerLogin = currentUser.Login ?? "unknown";

        if (requiredStatus == ImageTagChangeStatus.Pending)
        {
            if (request.Decision == TagChangeDecision.Approve)
            {
                if (!ImageTagChangeMapper.SameSnapshot(ImageTagChangeMapper.Snapshot(image.Tags),
                        change.PreviousTags))
                    throw new Base409ConflictException(
                        "The active tags changed since this proposal was submitted.");
                var tags = await repositories.AppImage.GetOrCreateTagsAsync(change.ProposedTags, now,
                    cancellationToken);
                if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
                    throw new Base409ConflictException("The proposal contains tags that are now rejected.");
                image.Tags = tags;
                change.Status = ImageTagChangeStatus.Approved;
                change.AppliedAtUtc = now;
            }
            else
            {
                change.Status = ImageTagChangeStatus.Rejected;
            }

            change.ReviewedByUserId = currentUser.UserId;
            change.ReviewedByLogin = reviewerLogin;
            change.ReviewedAtUtc = now;
            change.ReviewNote = note;
            await repositories.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ImageTagChangeMapper.ToDto(change);
        }

        if (await repositories.ImageTagChanges.GetPendingForImageAsync(image.Id, cancellationToken) is not null)
            throw new Base409ConflictException(
                "Resolve the pending tag change before reverting an applied change.");
        var latest = await repositories.ImageTagChanges.GetLatestApprovedForImageAsync(image.Id, cancellationToken);
        if (latest?.Id != change.Id)
            throw new Base409ConflictException("Only the latest applied tag change can be reverted.");
        if (!ImageTagChangeMapper.SameSnapshot(ImageTagChangeMapper.Snapshot(image.Tags), change.ProposedTags))
            throw new Base409ConflictException("The active tags no longer match this applied change.");
        var previousTags =
            await repositories.AppImage.GetOrCreateTagsAsync(change.PreviousTags, now, cancellationToken);
        if (previousTags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
            throw new Base409ConflictException("The previous tag set contains tags that are now rejected.");
        image.Tags = previousTags;
        change.Status = ImageTagChangeStatus.Reverted;
        change.RevertedByUserId = currentUser.UserId;
        change.RevertedByLogin = reviewerLogin;
        change.RevertedAtUtc = now;
        change.ReversionNote = note;
        await repositories.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ImageTagChangeMapper.ToDto(change);
    }
}