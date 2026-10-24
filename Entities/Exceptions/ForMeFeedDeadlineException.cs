namespace Entities.Exceptions;

public sealed class ForMeFeedDeadlineException() : Exception("Recommendation generation timed out. Retry later.");
