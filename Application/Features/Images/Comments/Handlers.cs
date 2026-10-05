using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Comments;

public sealed class GetCommentsHandler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<GetCommentsCommand, PageableCommentsDto>
{
    public async Task<PageableCommentsDto> Handle(GetCommentsCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var image = await repositories.AppImage.GetMetadataByIdAsync(request.ImageId, cancellationToken) ?? throw new Base404ReturnException("Image not found.");
        EnsurePublic(image);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.Interactions.GetCommentsAsync(image.Id, page, pageSize, cancellationToken);
        return new PageableCommentsDto(page, pageSize, result.Total, result.Comments.Select(ToDto).ToArray());
    }

    internal static CommentDto ToDto(Comment comment) => new(comment.Id, comment.ImageId, comment.AuthorId,
        comment.Author?.Login ?? string.Empty, comment.Content, comment.CreatedAtUtc);

    internal static void EnsurePublic(ImageMetadata image)
    {
        if (image.DeletedAtUtc is not null || image.Visibility != ImageVisibility.Gallery || image.ModerationStatus != ModerationStatus.Approved)
            throw new Base404ReturnException("Image not found.");
    }
}

public sealed class CreateCommentHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<CreateCommentCommand, CommentDto>
{
    public async Task<CommentDto> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var content = request.Request.Content?.Trim();
        if (content is null || content.Length is < 1 or > 2000) throw new Base400BadRequestException("Comment content must be between 1 and 2,000 characters.");
        var image = await repositories.AppImage.GetMetadataByIdAsync(request.ImageId, cancellationToken) ?? throw new Base404ReturnException("Image not found.");
        GetCommentsHandler.EnsurePublic(image);
        var now = clock.GetUtcNow();
        var restriction = await repositories.Interactions.GetCommentRestrictionAsync(currentUser.UserId!.Value, false, cancellationToken);
        if (restriction is not null && (restriction.RestrictedUntilUtc is null || restriction.RestrictedUntilUtc > now))
            throw new AppForbiddenException("You are restricted from writing comments.");
        var comment = new Comment { ImageId = image.Id, AuthorId = currentUser.UserId.Value, Content = content, CreatedAtUtc = now };
        await repositories.Interactions.AddCommentAsync(comment, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
        return new CommentDto(comment.Id, image.Id, comment.AuthorId, currentUser.Login ?? string.Empty, comment.Content, comment.CreatedAtUtc);
    }
}

public sealed class DeleteCommentHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<DeleteCommentCommand>
{
    public async Task Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        if (request.IsStaffDeletion && !ImageAuthorization.IsStaff(currentUser)) throw new AppForbiddenException("Staff access is required.");
        var comment = await repositories.Interactions.GetCommentAsync(request.CommentId, true, cancellationToken) ?? throw new Base404ReturnException("Comment not found.");
        if (comment.DeletedAtUtc is not null) throw new Base404ReturnException("Comment not found.");
        if (!request.IsStaffDeletion && comment.AuthorId != currentUser.UserId) throw new AppForbiddenException("You can only delete your own comments.");
        comment.DeletedAtUtc = clock.GetUtcNow();
        comment.DeletedByUserId = currentUser.UserId;
        await repositories.SaveAsync(cancellationToken);
    }
}
