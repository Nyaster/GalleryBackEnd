using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetForMe;

public sealed class Handler(IForMeFeedRepository feeds, IForMeFeedCursorProtector cursors,
    IUserContext currentUser, TimeProvider clock, ILogger<Handler> logger) : IRequestHandler<Command, ForMeFeedDto>
{
    public async Task<ForMeFeedDto> Handle(Command request, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new[] { new KeyValuePair<string, object?>("Operation",
            request.Cursor is null ? "CreateFeed" : "ReadFeedPage") });
        var startedAt = Stopwatch.GetTimestamp();
        var stage = "Authenticate";
        var outcome = "Succeeded";
        var level = LogLevel.Information;
        string? failureType = null;
        double? createMilliseconds = null;
        double? readMilliseconds = null;
        ForMeFeedDto? result = null;
        try
        {
            currentUser.RequireAuthenticated();
            var userId = currentUser.UserId!.Value;
            Guid feedId;
            var afterRank = 0;
            ForMeFeedCursor? cursor = null;
            if (request.Cursor is not null)
            {
                stage = "ValidateCursor";
                cursor = cursors.Unprotect(request.Cursor);
                if (cursor.UserId != userId) throw new Base404ReturnException("Recommendation feed not found.");
                if (cursor.ExpiresAtUtc <= clock.GetUtcNow())
                    throw new Base409ConflictException("Recommendation feed expired. Restart without a cursor.");
                feedId = cursor.FeedId;
                afterRank = cursor.LastRank;
            }
            else
            {
                stage = "CreateFeed";
                var filters = new ForMeFilters(
                    request.AiUsage?.Select(value => (int)value).Distinct().Order().ToArray() ?? [],
                    request.ExcludedTags?.Where(tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(tag => tag.Trim().ToLowerInvariant()).Distinct().Take(20).Order().ToArray() ?? []);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    try { feedId = await feeds.CreateAsync(userId, Math.Clamp(request.PageSize, 1, 50), filters, linked.Token); }
                    finally { createMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new ForMeFeedDeadlineException();
                }
            }

            stage = "ReadFeedPage";
            ForMeFeedPage page;
            var readStarted = Stopwatch.GetTimestamp();
            try { page = await feeds.ReadAsync(feedId, ImageAuthorization.GetViewer(currentUser), afterRank, cancellationToken); }
            finally { readMilliseconds = Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds; }
            stage = "ValidateCursorConsistency";
            if (cursor is not null && (cursor.PageSize != page.Feed.PageSize || cursor.ExpiresAtUtc != page.Feed.ExpiresAtUtc))
                throw new Base400BadRequestException("Cursor does not match the recommendation feed.");
            stage = "BuildResponse";
            var next = page.HasMore ? cursors.Protect(new ForMeFeedCursor(1, feedId, userId,
                page.LastRank, page.Feed.PageSize, page.Feed.ExpiresAtUtc)) : null;
            result = new ForMeFeedDto(page.Feed.Mode, page.Feed.CreatedAtUtc, page.Feed.ExpiresAtUtc,
                page.Feed.PageSize, page.Feed.Total, page.Images.Select(ImageDtoMapper.ToDto).ToArray(), next);
            return result;
        }
        catch (Exception exception)
        {
            failureType = exception.GetType().Name;
            (outcome, level) = exception switch
            {
                ForMeFeedDeadlineException => ("DeadlineExceeded", LogLevel.Warning),
                OperationCanceledException => ("Canceled", LogLevel.Warning),
                Base400BadRequestException or Base401UnauthorizedException or AppForbiddenException or
                    Base404ReturnException or Base409ConflictException or ForMeFeedLimitException => ("Rejected", LogLevel.Warning),
                _ => ("Failed", LogLevel.Error)
            };
            throw;
        }
        finally
        {
            var fields = new List<KeyValuePair<string, object?>>
            {
                new("EventName", "ForMeRequestHandled"), new("Outcome", outcome), new("Stage", stage),
                new("HandlerElapsedMilliseconds", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds),
                new("{OriginalFormat}", "{EventName}")
            };
            if (createMilliseconds is not null) fields.Add(new("CreateFeedElapsedMilliseconds", createMilliseconds));
            if (readMilliseconds is not null) fields.Add(new("ReadFeedPageElapsedMilliseconds", readMilliseconds));
            if (failureType is not null) fields.Add(new("FailureType", failureType));
            if (result is not null)
            {
                fields.Add(new("Mode", result.Mode));
                fields.Add(new("ReturnedImageCount", result.Images.Count));
                fields.Add(new("HasMore", result.NextCursor is not null));
            }
            logger.Log(level, new EventId(0, "ForMeRequestHandled"), fields, null, static (_, _) => "ForMeRequestHandled");
        }
    }
}
