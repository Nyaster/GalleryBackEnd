namespace Entities.Exceptions;

public sealed class ForMeFeedLimitException(string message, int retryAfterSeconds) : Exception(message)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
