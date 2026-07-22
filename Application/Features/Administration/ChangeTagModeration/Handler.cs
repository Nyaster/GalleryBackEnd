using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeTagModeration;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<Command, AdminTagDto>
{
    public async Task<AdminTagDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        if (request.Status is not (TagModerationStatus.Approved or TagModerationStatus.Rejected))
            throw new Base400BadRequestException("Tag moderation status must be Approved or Rejected.");
        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        try
        {
            var tag = await repositories.AppImage.GetTagByIdAsync(request.TagId, true, cancellationToken)
                ?? throw new Base404ReturnException("Tag not found.");
            tag.ModerationStatus = request.Status;
            if (request.Status == TagModerationStatus.Rejected)
            {
                var now = clock.GetUtcNow();
                var reviewerLogin = currentUser.Login ?? "unknown";
                var linkedImages = tag.AppImages.ToList();
                var changes = linkedImages.Select(image =>
                {
                    var previous = ImageTagChangeMapper.Snapshot(image.Tags);
                    return new ImageTagChange
                    {
                        ImageId = image.Id,
                        PreviousTags = previous,
                        ProposedTags = previous.Where(name => !string.Equals(name, tag.NormalizedName, StringComparison.Ordinal)).ToList(),
                        Kind = ImageTagChangeKind.GlobalTagRejection,
                        Status = ImageTagChangeStatus.Approved,
                        EditedByUserId = currentUser.UserId,
                        EditedByLogin = reviewerLogin,
                        CreatedAtUtc = now,
                        AppliedAtUtc = now,
                        ReviewedByUserId = currentUser.UserId,
                        ReviewedByLogin = reviewerLogin,
                        ReviewedAtUtc = now,
                        ReviewNote = $"Global tag '{tag.Name}' was rejected."
                    };
                }).ToList();
                var pending = await repositories.ImageTagChanges.GetPendingContainingTagAsync(tag.NormalizedName, cancellationToken);
                foreach (var change in pending)
                {
                    change.Status = ImageTagChangeStatus.Rejected;
                    change.ReviewedByUserId = currentUser.UserId;
                    change.ReviewedByLogin = reviewerLogin;
                    change.ReviewedAtUtc = now;
                    change.ReviewNote = $"Rejected automatically because the global tag '{tag.Name}' was rejected.";
                }
                foreach (var image in linkedImages)
                    image.Tags = image.Tags.Where(imageTag => imageTag.Id != tag.Id).ToList();
                await repositories.ImageTagChanges.AddRangeAsync(changes, cancellationToken);
                tag.AppImages.Clear();
            }
            await repositories.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AdminTagDto(tag.Id, tag.Name, tag.ModerationStatus, tag.CreatedAtUtc, tag.AppImages.Count);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
