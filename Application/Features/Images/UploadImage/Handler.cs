using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Microsoft.Extensions.Options;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.UploadImage;

public sealed class Handler(
    IRepositoryManager repositories,
    IImageStager stager,
    IUserContext currentUser,
    TimeProvider clock,
    IOptions<ImageStorageOptions> storageOptions) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var userId = currentUser.UserId ?? throw new AppForbiddenException("You do not have permission to upload images.");
        await EnsureUploadAllowedAsync(userId, cancellationToken);

        if (request.Request.AiUsage == AiUsageClassification.Unknown ||
            !Enum.IsDefined(request.Request.AiUsage))
            throw new ImageUploadValidationError("aiUsage must be HumanMade, AiAssisted, or AiGenerated.");

        var file = request.Request.ImageFile ?? throw new ImageUploadValidationError("An image file is required.");
        if (file.Length <= 0)
            throw new ImageUploadValidationError("The uploaded file is empty.");
        if (file.Length > storageOptions.Value.MaximumUploadMegabytes * 1024L * 1024L)
            throw new ImageUploadValidationError($"Image size may not exceed {storageOptions.Value.MaximumUploadMegabytes} MB.");
        await using var source = file.OpenReadStream();
        await using var staged = await stager.StageAsync(source, "uploads/", cancellationToken);
        UserMadeImage image;
        // Keep the tag status check and link writes atomic with global tag moderation.
        await using (var transaction = await repositories.BeginSerializableTransactionAsync(cancellationToken))
        {
            await EnsureUploadAllowedAsync(userId, cancellationToken);
            var now = clock.GetUtcNow();
            var tags = await repositories.AppImage.GetOrCreateTagsAsync(request.Request.Tags, now, cancellationToken);
            if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
                throw new Base400BadRequestException("Rejected tags cannot be used.");
            image = new UserMadeImage
            {
                Source = ImageSource.UserUpload,
                UploadedById = userId,
                UploadedAtUtc = now,
                Visibility = request.Request.IsPrivate ? ImageVisibility.Private : ImageVisibility.Gallery,
                ModerationStatus = currentUser.HasPermission(AccessPermission.UploadsBypassReview)
                    ? ModerationStatus.Approved
                    : ModerationStatus.Pending,
                AiUsage = request.Request.AiUsage,
                StorageKey = staged.StorageKey,
                ContentType = staged.Metadata.ContentType,
                Width = staged.Metadata.Width,
                Height = staged.Metadata.Height,
                Tags = tags,
                EmbeddingStatus = EmbeddingStatus.Pending
            };
            await repositories.AppImage.AddAsync(image, cancellationToken);
            await repositories.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            staged.MarkPersisted();
        }

        var card = await repositories.AppImage.GetManagementCardByIdAsync(image.Id,
                ImageAuthorization.GetViewer(currentUser), cancellationToken)
            ?? throw new InvalidOperationException("The saved image could not be read.");
        return ImageDtoMapper.ToDto(card, currentUser);
    }

    private async Task EnsureUploadAllowedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await repositories.AppUser.GetByIdAsync(userId, false, cancellationToken);
        if (user is null ||
            !UploadAccess.IsAllowed(user, currentUser.HasPermission(AccessPermission.UploadsBypassRestrictions)))
            throw new AppForbiddenException("You do not have permission to upload images.");
    }
}
