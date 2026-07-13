using Entities.Models;

namespace Contracts;

public interface IInteractionRepository
{
    Task<(List<Comment> Comments, int Total)> GetCommentsAsync(int imageId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Comment?> GetCommentAsync(int commentId, bool trackChanges, CancellationToken cancellationToken = default);
    Task AddCommentAsync(Comment comment, CancellationToken cancellationToken = default);
    Task<CommentRestriction?> GetCommentRestrictionAsync(int userId, bool trackChanges, CancellationToken cancellationToken = default);
    Task AddCommentRestrictionAsync(CommentRestriction restriction, CancellationToken cancellationToken = default);
    void RemoveCommentRestriction(CommentRestriction restriction);
    Task<int> CountCommentsCreatedSinceAsync(int userId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task<ImageLike?> GetLikeAsync(int imageId, int userId, bool trackChanges, CancellationToken cancellationToken = default);
    Task AddLikeAsync(ImageLike like, CancellationToken cancellationToken = default);
    void RemoveLike(ImageLike like);
    Task AddLikeActivityAsync(ImageLikeActivity activity, CancellationToken cancellationToken = default);
    Task<bool> SetLikeAsync(int imageId, int userId, bool isLiked, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> GetLikeCountAsync(int imageId, CancellationToken cancellationToken = default);
}
