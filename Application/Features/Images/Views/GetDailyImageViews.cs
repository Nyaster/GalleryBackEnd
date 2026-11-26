using System.Globalization;
using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Views;

public sealed record GetDailyImageViews(int ImageId, string? From, string? Through)
    : IRequest<IReadOnlyList<ImageViewDay>>;

public sealed class GetDailyImageViewsHandler(
    IRepositoryManager repositories,
    IUserContext currentUser,
    IImageViewRepository views,
    TimeProvider clock) : IRequestHandler<GetDailyImageViews, IReadOnlyList<ImageViewDay>>
{
    public async Task<IReadOnlyList<ImageViewDay>> Handle(GetDailyImageViews request,
        CancellationToken cancellationToken)
    {
        var (from, through) = ResolveRange(request.From, request.Through);
        await ImageAuthorization.GetReadableMetadataAsync(repositories.AppImage, request.ImageId, currentUser,
            cancellationToken);
        var days = await views.GetDailyAsync(request.ImageId, from, through, cancellationToken);
        var countsByDate = days.ToDictionary(day => day.Date, day => day.NewUniqueViewers);
        var result = new ImageViewDay[through.DayNumber - from.DayNumber + 1];
        for (var index = 0; index < result.Length; index++)
        {
            var date = from.AddDays(index);
            result[index] = new ImageViewDay(date, countsByDate.GetValueOrDefault(date));
        }

        return result;
    }

    private (DateOnly From, DateOnly Through) ResolveRange(string? fromValue, string? throughValue)
    {
        if (fromValue is null && throughValue is null)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return (DateOnly.FromDayNumber(Math.Max(0, today.DayNumber - 29)), today);
        }

        if (!DateOnly.TryParseExact(fromValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var from) ||
            !DateOnly.TryParseExact(throughValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var through))
            throw new Base400BadRequestException("Supply both from and through in YYYY-MM-DD format.");

        if (through.DayNumber - from.DayNumber is < 0 or >= 366)
            throw new Base400BadRequestException("The inclusive date range must span 1 to 366 days.");

        return (from, through);
    }
}