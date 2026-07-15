namespace Shared.DataTransferObjects;

public sealed record UpdateAnnouncementDto(string? Content);
public sealed record AnnouncementDto(string? Content, DateTimeOffset? UpdatedAtUtc);
public sealed record CreateFeedbackDto(string? Content);
public sealed record FeedbackDto(int Id, int AuthorId, string AuthorLogin, string Content, DateTimeOffset CreatedAtUtc);
public sealed record PageableFeedbackDto(int Page, int PageSize, int Total, IReadOnlyList<FeedbackDto> Feedback);
