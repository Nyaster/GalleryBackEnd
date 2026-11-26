namespace Shared.DataTransferObjects;

public sealed record ImageViewTotals(long TotalViews, long UniqueRegisteredViewers);

public sealed record ImageViewDay(DateOnly Date, long NewUniqueViewers);
