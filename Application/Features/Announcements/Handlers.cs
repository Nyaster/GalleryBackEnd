using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Announcements;

public sealed class GetAnnouncementHandler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<GetAnnouncementCommand, AnnouncementDto>
{
    public async Task<AnnouncementDto> Handle(GetAnnouncementCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var announcement = await repositories.Announcements.GetAsync(false, cancellationToken);
        return announcement is null ? new AnnouncementDto(null, null) : new AnnouncementDto(announcement.Content, announcement.UpdatedAtUtc);
    }
}

public sealed class UpdateAnnouncementHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<UpdateAnnouncementCommand, AnnouncementDto>
{
    public async Task<AnnouncementDto> Handle(UpdateAnnouncementCommand request, CancellationToken cancellationToken)
    {
        EnsureAdministrator(currentUser);
        var content = ValidateContent(request.Request.Content, "Announcement");
        var now = clock.GetUtcNow();
        var announcement = await repositories.Announcements.GetAsync(true, cancellationToken);
        if (announcement is null)
        {
            announcement = new Announcement { Id = 1, Content = content, UpdatedAtUtc = now, UpdatedByUserId = currentUser.UserId!.Value };
            await repositories.Announcements.AddAsync(announcement, cancellationToken);
        }
        else
        {
            announcement.Content = content;
            announcement.UpdatedAtUtc = now;
            announcement.UpdatedByUserId = currentUser.UserId!.Value;
        }
        await repositories.SaveAsync(cancellationToken);
        return new AnnouncementDto(announcement.Content, announcement.UpdatedAtUtc);
    }

    internal static void EnsureAdministrator(IUserContext currentUser)
    {
        currentUser.RequireAuthenticated();
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin)))
            throw new AppForbiddenException("Administrator access is required.");
    }

    internal static string ValidateContent(string? content, string subject)
    {
        var trimmed = content?.Trim();
        if (trimmed is null || trimmed.Length is < 1 or > 2000)
            throw new Base400BadRequestException($"{subject} content must be between 1 and 2,000 characters.");
        return trimmed;
    }
}

public sealed class ClearAnnouncementHandler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<ClearAnnouncementCommand>
{
    public async Task Handle(ClearAnnouncementCommand request, CancellationToken cancellationToken)
    {
        UpdateAnnouncementHandler.EnsureAdministrator(currentUser);
        var announcement = await repositories.Announcements.GetAsync(true, cancellationToken);
        if (announcement is null) return;
        repositories.Announcements.Remove(announcement);
        await repositories.SaveAsync(cancellationToken);
    }
}

public sealed class CreateFeedbackHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<CreateFeedbackCommand, FeedbackDto>
{
    public async Task<FeedbackDto> Handle(CreateFeedbackCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var feedback = new Feedback
        {
            AuthorId = currentUser.UserId!.Value,
            Content = UpdateAnnouncementHandler.ValidateContent(request.Request.Content, "Feedback"),
            CreatedAtUtc = clock.GetUtcNow()
        };
        await repositories.Feedback.AddAsync(feedback, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
        return ToDto(feedback, currentUser.Login ?? string.Empty);
    }

    internal static FeedbackDto ToDto(Feedback feedback, string authorLogin)
        => new(feedback.Id, feedback.AuthorId, authorLogin, feedback.Content, feedback.CreatedAtUtc);
}

public sealed class GetFeedbackHandler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<GetFeedbackCommand, PageableFeedbackDto>
{
    public async Task<PageableFeedbackDto> Handle(GetFeedbackCommand request, CancellationToken cancellationToken)
    {
        UpdateAnnouncementHandler.EnsureAdministrator(currentUser);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.Feedback.GetAsync(page, pageSize, cancellationToken);
        return new PageableFeedbackDto(page, pageSize, result.Total, result.Feedback.Select(feedback =>
            CreateFeedbackHandler.ToDto(feedback, feedback.Author?.Login ?? string.Empty)).ToArray());
    }
}
