using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Rankings;

public sealed class GetRankingsHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock)
    : IRequestHandler<GetRankingsCommand, PageableRankingsDto>
{
    public async Task<PageableRankingsDto> Handle(GetRankingsCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var current = RankingPeriodBounds.Current(request.Period, clock.GetUtcNow());
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var viewer = ImageAuthorization.GetViewer(currentUser);
        if (request.PeriodStartUtc is null)
        {
            var live = await repositories.Rankings.GetLivePageAsync(current.StartUtc, current.EndUtc,
                page, pageSize, viewer, request.AiUsage, cancellationToken);
            return Response(request.Period, current.StartUtc, current.EndUtc, false, page, pageSize, live);
        }
        var start = request.PeriodStartUtc.Value.ToUniversalTime();
        if (request.PeriodStartUtc.Value.Offset != TimeSpan.Zero || !RankingPeriodBounds.IsBoundary(request.Period, start) || start >= current.StartUtc)
            throw new Base400BadRequestException("periodStartUtc must be the start of a completed UTC period.");
        var snapshot = await repositories.Rankings.GetSnapshotAsync(request.Period, start, cancellationToken)
            ?? throw new Base404ReturnException("Ranking archive not found.");
        var archive = await repositories.Rankings.GetArchivePageAsync(snapshot.Id, page, pageSize, viewer,
            request.AiUsage, cancellationToken);
        return Response(request.Period, snapshot.PeriodStartUtc, snapshot.PeriodEndUtc, true, page, pageSize, archive);
    }

    private static PageableRankingsDto Response(RankingPeriod period, DateTimeOffset start, DateTimeOffset end,
        bool archived, int page, int pageSize, RankingPage result)
        => new(period, start, end, archived, page, pageSize, result.Total,
            result.Entries.Select(entry => new RankingEntryDto(entry.Rank, entry.LikeDelta,
                ImageDtoMapper.ToDto(entry.Image))).ToArray());
}
