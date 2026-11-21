using Shared.DataTransferObjects;

namespace Contracts;

public interface IContentVisibilityRuleRepository
{
    Task<List<ContentVisibilityRuleDto>> ListAsync(CancellationToken cancellationToken);
    Task<ContentVisibilityRuleDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<ContentVisibilityRuleDto> SaveAsync(int? id, int[] tagIds, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
