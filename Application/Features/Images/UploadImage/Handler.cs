using Application.Helpers;
using Contracts;
using Entities.Models;
using MediatR;
using Microsoft.Extensions.Options;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.UploadImage;

public sealed class Handler(
    IRepositoryManager repositories,
    IImageStorage storage,
    IImageProcessor processor,
    IUserContext currentUser,
    TimeProvider clock,
    IOptions<ImageStorageOptions> storageOptions) : IRequestHandler<Command, AppImageDto>
{
    public async Task<AppImageDto> Handle(Command request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var file = request.Request.ImageFile ?? throw new Entities.Exceptions.ImageUploadValidationError("An image file is required.");
        if (file.Length <= 0)
            throw new Entities.Exceptions.ImageUploadValidationError("The uploaded file is empty.");
        if (file.Length > storageOptions.Value.MaximumUploadMegabytes * 1024L * 1024L)
            throw new Entities.Exceptions.ImageUploadValidationError($"Image size may not exceed {storageOptions.Value.MaximumUploadMegabytes} MB.");
        string? temporaryPath = null;
        string? storageKey = null;
        try
        {
            await using var source = file.OpenReadStream();
            temporaryPath = await storage.SaveTemporaryAsync(source, cancellationToken);
            var inspected = await processor.InspectAsync(temporaryPath, cancellationToken);
            storageKey = $"uploads/{Guid.NewGuid():N}{inspected.Extension}";
            await storage.MoveTemporaryToFinalAsync(temporaryPath, storageKey, cancellationToken);
            temporaryPath = null;

            var now = clock.GetUtcNow();
            var tags = await repositories.AppImage.GetOrCreateTagsAsync(request.Request.Tags, now, cancellationToken);
            if (tags.Any(tag => tag.ModerationStatus == TagModerationStatus.Rejected))
                throw new Entities.Exceptions.Base400BadRequestException("Rejected tags cannot be used.");
            var image = new UserMadeImage
            {
                Source = ImageSource.UserUpload,
                UploadedById = currentUser.UserId,
                UploadedAtUtc = now,
                Visibility = request.Request.IsPrivate ? ImageVisibility.Private : ImageVisibility.Gallery,
                ModerationStatus = ModerationStatus.Pending,
                StorageKey = storageKey,
                ContentType = inspected.ContentType,
                Width = inspected.Width,
                Height = inspected.Height,
                Tags = tags,
                EmbeddingStatus = EmbeddingStatus.Pending
            };
            await repositories.AppImage.AddAsync(image, cancellationToken);
            await repositories.SaveAsync(cancellationToken);
            image.UploadedBy = new AppUser { Id = currentUser.UserId!.Value, Login = currentUser.Login!, NormalizedLogin = string.Empty, PasswordHash = string.Empty };
            return ImageDtoMapper.ToDto(image, currentUser);
        }
        catch
        {
            if (storageKey is not null)
                await storage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
