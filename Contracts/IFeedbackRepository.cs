using Entities.Models;
using Shared.DataTransferObjects;

namespace Contracts;

public interface IFeedbackRepository
{
    Task AddAsync(Feedback feedback, CancellationToken cancellationToken = default);
    Task<(List<FeedbackDto> Feedback, int Total)> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
