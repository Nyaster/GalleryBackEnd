using Entities.Models;

namespace Contracts;

public interface IAnnouncementRepository
{
    Task<Announcement?> GetAsync(bool trackChanges, CancellationToken cancellationToken = default);
    Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default);
    void Remove(Announcement announcement);
}
