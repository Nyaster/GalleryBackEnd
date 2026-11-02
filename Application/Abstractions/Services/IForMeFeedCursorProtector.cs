namespace Service.Contracts;

public sealed record ForMeFeedCursor(int Version, Guid FeedId, int UserId, int LastRank, int PageSize,
    DateTimeOffset ExpiresAtUtc);

public interface IForMeFeedCursorProtector
{
    string Protect(ForMeFeedCursor cursor);
    ForMeFeedCursor Unprotect(string token);
}
