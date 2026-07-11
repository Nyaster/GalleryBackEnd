using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeTagModeration;

public sealed record Command(int TagId, TagModerationStatus Status) : IRequest<AdminTagDto>;
