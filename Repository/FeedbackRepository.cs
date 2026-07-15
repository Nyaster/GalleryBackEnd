using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class FeedbackRepository(RepositoryContext context) : IFeedbackRepository
{
    public Task AddAsync(Feedback feedback, CancellationToken cancellationToken = default)
        => context.Feedback.AddAsync(feedback, cancellationToken).AsTask();

    public async Task<(List<Feedback> Feedback, int Total)> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.Feedback.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var feedback = await query.Include(item => item.Author).OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
            .Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 50)).Take(Math.Clamp(pageSize, 1, 50)).ToListAsync(cancellationToken);
        return (feedback, total);
    }
}
