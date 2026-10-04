using System.Globalization;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Discovery;

public sealed class GetDiscoveryHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock)
    : IRequestHandler<GetDiscoveryCommand, PageableDiscoveryDto>
{
    public async Task<PageableDiscoveryDto> Handle(GetDiscoveryCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var now = clock.GetUtcNow();
        var collection = request.Collection switch
        {
            "most-liked" => DiscoveryCollection.MostLiked,
            "new-uploads" => DiscoveryCollection.NewUploads,
            "most-commented" => DiscoveryCollection.MostCommented,
            _ => throw new Base400BadRequestException("collection must be 'most-liked', 'new-uploads', or 'most-commented'.")
        };

        var timeZone = TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var date = today;
        if (request.Date is not null &&
            (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date) || date > today || date == DateOnly.MaxValue))
            throw new Base400BadRequestException("date must be a valid YYYY-MM-DD date on or before today in UTC.");

        // Convert each local midnight independently so future site timezones can have 23/25-hour days.
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.Discovery.GetCollectionAsync(collection, startUtc, endUtc,
            currentUser.UserId!.Value, page, pageSize, request.AiUsage, cancellationToken);

        return new PageableDiscoveryDto(request.Collection, "Daily", date, timeZone.Id, startUtc, endUtc,
            now.UtcDateTime, result.GalleryTotal, page, pageSize, result.Total, result.Entries);
    }
}
