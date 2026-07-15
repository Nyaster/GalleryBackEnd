using Contracts;
using Entities.Models;
using Microsoft.EntityFrameworkCore;

namespace Repository;

public sealed class AnnouncementRepository(RepositoryContext context) : IAnnouncementRepository
{
    public Task<Announcement?> GetAsync(bool trackChanges, CancellationToken cancellationToken = default)
        => (trackChanges ? context.Announcements : context.Announcements.AsNoTracking())
            .SingleOrDefaultAsync(announcement => announcement.Id == 1, cancellationToken);

    public Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default)
        => context.Announcements.AddAsync(announcement, cancellationToken).AsTask();

    public void Remove(Announcement announcement) => context.Announcements.Remove(announcement);
}
