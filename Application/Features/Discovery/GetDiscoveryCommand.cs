using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Discovery;

public sealed record GetDiscoveryCommand(string Collection, string? Date = null, int Page = 1, int PageSize = 20,
    IReadOnlyList<AiUsageClassification>? AiUsage = null) : IRequest<PageableDiscoveryDto>;
