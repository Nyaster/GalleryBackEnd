using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Rankings;

public sealed record GetRankingsCommand(RankingPeriod Period, DateTimeOffset? PeriodStartUtc, int Page, int PageSize,
    IReadOnlyList<AiUsageClassification>? AiUsage = null) : IRequest<PageableRankingsDto>;
