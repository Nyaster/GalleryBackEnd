using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Announcements;

public sealed record GetAnnouncementCommand : IRequest<AnnouncementDto>;
public sealed record UpdateAnnouncementCommand(UpdateAnnouncementDto Request) : IRequest<AnnouncementDto>;
public sealed record ClearAnnouncementCommand : IRequest;
public sealed record CreateFeedbackCommand(CreateFeedbackDto Request) : IRequest<FeedbackDto>;
public sealed record GetFeedbackCommand(int Page, int PageSize) : IRequest<PageableFeedbackDto>;
