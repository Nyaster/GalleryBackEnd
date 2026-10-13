using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace Repository;

public sealed class FeedbackRepository(RepositoryContext context) : IFeedbackRepository
{
    public Task AddAsync(Feedback feedback, CancellationToken cancellationToken = default)
        => context.Feedback.AddAsync(feedback, cancellationToken).AsTask();

    public async Task<(List<FeedbackDto> Feedback, int Total)> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.Feedback.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var size = Math.Clamp(pageSize, 1, 50);
        var offset = ((long)Math.Max(page, 1) - 1) * size;
        if (offset >= total) return ([], total);
        var feedback = await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((int)offset).Take(size)
            .Select(item => new FeedbackDto(item.Id, item.AuthorId, item.Author!.Login ?? string.Empty,
                item.Content, item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return (feedback, total);
    }
}
