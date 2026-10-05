using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.ReplaceImageTags;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<Command, ImageTagChangeDto>
{
    public async Task<ImageTagChangeDto> Handle(Command request, CancellationToken cancellationToken)
    {
        await using var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken);
        try
        {
            var image = await repositories.AppImage.GetWithTagsByIdAsync(request.ImageId, cancellationToken)
                ?? throw new Base404ReturnException("Image not found.");
            ImageAuthorization.EnsureCanManage(image, currentUser);

            if (await repositories.ImageTagChanges.GetPendingForImageAsync(image.Id, cancellationToken) is not null)
                throw new Base409ConflictException("This image already has a pending tag change.");

            var tags = await repositories.AppImage.GetOrCreateTagsAsync(request.Request.Tags ?? [], clock.GetUtcNow(), cancellationToken);
            if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
                throw new Base400BadRequestException("Rejected tags cannot be used.");

            var previous = ImageTagChangeMapper.Snapshot(image.Tags);
            var proposed = ImageTagChangeMapper.Snapshot(tags);
            if (ImageTagChangeMapper.SameSnapshot(previous, proposed))
                throw new Base400BadRequestException("The proposed tags are identical to the active tags.");

            var now = clock.GetUtcNow();
            var staff = ImageAuthorization.IsStaff(currentUser);
            var change = new ImageTagChange
            {
                ImageId = image.Id,
                PreviousTags = previous,
                ProposedTags = proposed,
                Kind = ImageTagChangeKind.Replacement,
                Status = staff ? ImageTagChangeStatus.Approved : ImageTagChangeStatus.Pending,
                EditedByUserId = currentUser.UserId,
                EditedByLogin = currentUser.Login ?? "unknown",
                CreatedAtUtc = now,
                AppliedAtUtc = staff ? now : null,
                ReviewedByUserId = staff ? currentUser.UserId : null,
                ReviewedByLogin = staff ? currentUser.Login ?? "unknown" : null,
                ReviewedAtUtc = staff ? now : null
            };

            if (staff) image.Tags = tags;
            await repositories.ImageTagChanges.AddAsync(change, cancellationToken);
            await repositories.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ImageTagChangeMapper.ToDto(change);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
