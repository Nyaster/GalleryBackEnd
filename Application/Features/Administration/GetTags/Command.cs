using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetTags;

public sealed record Command(TagModerationStatus Status, int Page = 1, int PageSize = 20) : IRequest<PageableTagsDto>;
