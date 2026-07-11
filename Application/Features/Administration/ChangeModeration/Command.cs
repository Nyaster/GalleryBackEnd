using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.ChangeModeration;

public sealed record Command(int ImageId, ModerationStatus Status) : IRequest<AppImageDto>;
