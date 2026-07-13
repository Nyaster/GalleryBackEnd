using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Rankings;

public sealed class GetRankingsHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<GetRankingsCommand, PageableRankingsDto>
{
    public async Task<PageableRankingsDto> Handle(GetRankingsCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var now = clock.GetUtcNow();
        var current = RankingPeriodBounds.Current(request.Period, now);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        if (request.PeriodStartUtc is null)
            return await Live(request.Period, current.StartUtc, current.EndUtc, page, pageSize, cancellationToken);
        var start = request.PeriodStartUtc.Value.ToUniversalTime();
        if (request.PeriodStartUtc.Value.Offset != TimeSpan.Zero || !RankingPeriodBounds.IsBoundary(request.Period, start) || start >= current.StartUtc)
            throw new Base400BadRequestException("periodStartUtc must be the start of a completed UTC period.");
        var snapshot = await repositories.Rankings.GetSnapshotAsync(request.Period, start, false, cancellationToken)
            ?? throw new Base404ReturnException("Ranking archive not found.");
        var entries = await repositories.Rankings.GetSnapshotEntriesAsync(snapshot.Id, cancellationToken);
        var filtered = entries.Where(item => IsDiscoverable(item.Image)).OrderBy(item => item.Entry.Rank).ToList();
        return Page(request.Period, snapshot.PeriodStartUtc, snapshot.PeriodEndUtc, true, page, pageSize, filtered.Select(item =>
            new RankingEntryDto(item.Entry.Rank, item.Entry.LikeDelta, ImageDtoMapper.ToDto(item.Image, currentUser))).ToList());
    }

    private async Task<PageableRankingsDto> Live(RankingPeriod period, DateTimeOffset start, DateTimeOffset end, int page, int pageSize, CancellationToken cancellationToken)
    {
        var scores = await repositories.Rankings.GetLiveScoresAsync(start, end, cancellationToken);
        var images = await repositories.Rankings.GetDiscoverableImagesAsync(scores.Select(score => score.ImageId), cancellationToken);
        var byId = images.ToDictionary(image => image.Id);
        var entries = scores.Where(score => byId.ContainsKey(score.ImageId)).OrderByDescending(score => score.LikeDelta).ThenByDescending(score => score.UploadedAtUtc).ThenByDescending(score => score.ImageId)
            .Select((score, index) => new RankingEntryDto(index + 1, score.LikeDelta, ImageDtoMapper.ToDto(byId[score.ImageId], currentUser))).ToList();
        return Page(period, start, end, false, page, pageSize, entries);
    }

    private static PageableRankingsDto Page(RankingPeriod period, DateTimeOffset start, DateTimeOffset end, bool archived, int page, int pageSize, List<RankingEntryDto> entries)
        => new(period, start, end, archived, page, pageSize, entries.Count, entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
    private static bool IsDiscoverable(AppImage image) => image.DeletedAtUtc is null && image.Visibility == ImageVisibility.Gallery && image.ModerationStatus == ModerationStatus.Approved;
}
