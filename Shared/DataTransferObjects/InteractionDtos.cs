using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record CreateCommentDto(string? Content);
public sealed record CommentDto(int Id, int ImageId, int AuthorId, string AuthorLogin, string Content, DateTimeOffset CreatedAtUtc);
public sealed record PageableCommentsDto(int Page, int PageSize, int Total, IReadOnlyList<CommentDto> Comments);
public sealed record CommentRestrictionUpdateDto(DateTimeOffset? RestrictedUntilUtc);
public sealed record CommentRestrictionDto(int UserId, string Login, bool IsRestricted, DateTimeOffset? RestrictedUntilUtc);
public sealed record LikeSummaryDto(int LikeCount, bool IsLikedByCurrentUser);
public sealed record RankingEntryDto(int Rank, int LikeDelta, AppImageDto Image);
public sealed record PageableRankingsDto(RankingPeriod Period, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc,
    bool IsArchived, int Page, int PageSize, int Total, IReadOnlyList<RankingEntryDto> Entries);
