using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class InteractionRepository(RepositoryContext context) : IInteractionRepository
{
    public async Task<(List<Comment> Comments, int Total)> GetCommentsAsync(int imageId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.Comments.AsNoTracking().Where(comment => comment.ImageId == imageId && comment.DeletedAtUtc == null);
        var total = await query.CountAsync(cancellationToken);
        var comments = await query.Include(comment => comment.Author).OrderBy(comment => comment.CreatedAtUtc).ThenBy(comment => comment.Id)
            .Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 50)).Take(Math.Clamp(pageSize, 1, 50)).ToListAsync(cancellationToken);
        return (comments, total);
    }

    public Task<Comment?> GetCommentAsync(int commentId, bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.Comments : context.Comments.AsNoTracking()).Include(comment => comment.Image)
            .SingleOrDefaultAsync(comment => comment.Id == commentId, cancellationToken);

    public Task AddCommentAsync(Comment comment, CancellationToken cancellationToken = default) => context.Comments.AddAsync(comment, cancellationToken).AsTask();
    public Task<CommentRestriction?> GetCommentRestrictionAsync(int userId, bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.CommentRestrictions : context.CommentRestrictions.AsNoTracking()).SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
    public Task AddCommentRestrictionAsync(CommentRestriction restriction, CancellationToken cancellationToken = default) => context.CommentRestrictions.AddAsync(restriction, cancellationToken).AsTask();
    public void RemoveCommentRestriction(CommentRestriction restriction) => context.CommentRestrictions.Remove(restriction);
    public Task<int> CountCommentsCreatedSinceAsync(int userId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
        => context.Comments.CountAsync(comment => comment.AuthorId == userId && comment.CreatedAtUtc >= sinceUtc, cancellationToken);
    public Task<ImageLike?> GetLikeAsync(int imageId, int userId, bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.ImageLikes : context.ImageLikes.AsNoTracking()).SingleOrDefaultAsync(like => like.ImageId == imageId && like.UserId == userId, cancellationToken);
    public Task AddLikeAsync(ImageLike like, CancellationToken cancellationToken = default) => context.ImageLikes.AddAsync(like, cancellationToken).AsTask();
    public void RemoveLike(ImageLike like) => context.ImageLikes.Remove(like);
    public Task AddLikeActivityAsync(ImageLikeActivity activity, CancellationToken cancellationToken = default) => context.ImageLikeActivities.AddAsync(activity, cancellationToken).AsTask();
    public async Task<bool> SetLikeAsync(int imageId, int userId, bool isLiked, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var changed = isLiked
            ? await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikes" ("ImageId", "UserId", "CreatedAtUtc") VALUES ({imageId}, {userId}, {now})
                ON CONFLICT ("ImageId", "UserId") DO NOTHING
                """, cancellationToken) == 1
            : await context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "ImageLikes" WHERE "ImageId" = {imageId} AND "UserId" = {userId}
                """, cancellationToken) == 1;
        if (changed)
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ImageLikeActivities" ("ImageId", "UserId", "OccurredAtUtc", "Type") VALUES ({imageId}, {userId}, {now}, {(isLiked ? "Like" : "Unlike")})
                """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
    public Task<int> GetLikeCountAsync(int imageId, CancellationToken cancellationToken = default) => context.ImageLikes.CountAsync(like => like.ImageId == imageId, cancellationToken);
}
