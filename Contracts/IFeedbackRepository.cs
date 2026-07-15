using Entities.Models;

namespace Contracts;

public interface IFeedbackRepository
{
    Task AddAsync(Feedback feedback, CancellationToken cancellationToken = default);
    Task<(List<Feedback> Feedback, int Total)> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
