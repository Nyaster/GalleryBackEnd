using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ModerateImageTagChange;

public sealed record Command(long ChangeId, TagChangeDecision Decision, string? Note) : IRequest<ImageTagChangeDto>;
