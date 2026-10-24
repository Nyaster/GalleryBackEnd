using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetForMe;

public sealed record Command(int PageSize = 20, IReadOnlyList<AiUsageClassification>? AiUsage = null,
    IReadOnlyList<string>? ExcludedTags = null, string? Cursor = null) : IRequest<ForMeFeedDto>;
