using System.Diagnostics;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Repository;
using Shared.DataTransferObjects;
using Xunit.Abstractions;

namespace GallerySiteIntegrationTests;

public sealed class InteractionReadPerformanceTests(PostgresFixture database, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ReadPages_MixedRows_PreserveValuesOrderAndBoundsWithoutHydratingEntities()
    {
        var seed = await SeedAsync();
        var capture = new InteractionReadCapture();
        await using var context = database.CreateContext(capture);
        var comments = new InteractionRepository(context);
        var feedback = new FeedbackRepository(context);
        var expectedComments = seed.Comments.Where(item => item.DeletedAtUtc is null)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToArray();
        var expectedFeedback = seed.Feedback.OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id).ToArray();

        var first = await comments.GetCommentsAsync(seed.ImageId, int.MinValue, int.MaxValue);
        Assert.Equal(expectedComments.Length, first.Total);
        Assert.Equal(expectedComments.Take(50).Select(item => item.Id), first.Comments.Select(item => item.Id));
        Assert.Equal(expectedComments.Skip(50).Take(50).Select(item => item.Id),
            (await comments.GetCommentsAsync(seed.ImageId, 2, 50)).Comments.Select(item => item.Id));
        Assert.Equal(expectedComments.Skip(100).Select(item => item.Id),
            (await comments.GetCommentsAsync(seed.ImageId, 3, 50)).Comments.Select(item => item.Id));
        Assert.Equal(expectedComments[0].Id,
            Assert.Single((await comments.GetCommentsAsync(seed.ImageId, 0, int.MinValue)).Comments).Id);

        var newest = await feedback.GetAsync(0, int.MaxValue);
        Assert.Equal(expectedFeedback.Length, newest.Total);
        Assert.Equal(expectedFeedback.Take(50).Select(item => item.Id), newest.Feedback.Select(item => item.Id));
        Assert.Equal(expectedFeedback.Skip(50).Take(50).Select(item => item.Id),
            (await feedback.GetAsync(2, 50)).Feedback.Select(item => item.Id));
        Assert.Equal(expectedFeedback.Skip(100).Select(item => item.Id),
            (await feedback.GetAsync(3, 50)).Feedback.Select(item => item.Id));
        Assert.Equal(expectedFeedback[0].Id, Assert.Single((await feedback.GetAsync(-1, 0)).Feedback).Id);

        var comment = first.Comments[0];
        Assert.Equal(seed.ImageId, comment.ImageId);
        Assert.Equal(expectedComments[0].AuthorId, comment.AuthorId);
        Assert.Equal(seed.Login, comment.AuthorLogin);
        Assert.Equal(expectedComments[0].Content, comment.Content);
        Assert.Equal(expectedComments[0].CreatedAtUtc, comment.CreatedAtUtc);
        var item = newest.Feedback[0];
        Assert.Equal(expectedFeedback[0].AuthorId, item.AuthorId);
        Assert.Equal(seed.Login, item.AuthorLogin);
        Assert.Equal(expectedFeedback[0].Content, item.Content);
        Assert.Equal(expectedFeedback[0].CreatedAtUtc, item.CreatedAtUtc);
        // Both methods must read only response fields, including the author's login.
        Assert.Empty(capture.Entities);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.All(capture.SelectedColumns.Where(columns => columns.Length > 1), columns =>
        {
            Assert.Equal(columns.Length == 6
                ? new[] { "Id", "ImageId", "AuthorId", "Login", "Content", "CreatedAtUtc" }
                : new[] { "Id", "AuthorId", "Login", "Content", "CreatedAtUtc" }, columns);
        });

        foreach (var page in new[] { 4, int.MaxValue })
        {
            var readCount = capture.SelectedColumns.Count;
            var emptyComments = await comments.GetCommentsAsync(seed.ImageId, page, 50);
            Assert.Empty(emptyComments.Comments);
            Assert.Equal(expectedComments.Length, emptyComments.Total);
            var emptyFeedback = await feedback.GetAsync(page, 50);
            Assert.Empty(emptyFeedback.Feedback);
            Assert.Equal(expectedFeedback.Length, emptyFeedback.Total);
            Assert.Equal(readCount + 2, capture.SelectedColumns.Count);
        }

        Assert.Empty((await comments.GetCommentsAsync(int.MaxValue, 1, 50)).Comments);
    }

    [Fact]
    public async Task ReadPages_DifferentAuthorWithEmptyLogin_PreservesIdentityAndEmptyLogin()
    {
        var seed = await SeedAsync();
        var firstComment = seed.Comments.Where(item => item.DeletedAtUtc is null)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).First();
        var newestFeedback = seed.Feedback.OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id).First();
        int authorId;
        await using (var writer = database.CreateContext())
        {
            var author = new AppUser { Login = string.Empty, NormalizedLogin = Guid.NewGuid().ToString("N"), PasswordHash = "unused" };
            writer.AppUsers.Add(author);
            await writer.SaveChangesAsync();
            authorId = author.Id;
            await writer.Comments.Where(item => item.Id == firstComment.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.AuthorId, authorId));
            await writer.Feedback.Where(item => item.Id == newestFeedback.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.AuthorId, authorId));
        }
        var capture = new InteractionReadCapture();
        await using var reader = database.CreateContext(capture);
        var comments = await new InteractionRepository(reader).GetCommentsAsync(seed.ImageId, 1, 2);
        Assert.Equal(new CommentDto(firstComment.Id, seed.ImageId, authorId, string.Empty,
            firstComment.Content, firstComment.CreatedAtUtc), comments.Comments[0]);
        Assert.Equal(seed.Login, comments.Comments[1].AuthorLogin);
        var feedback = await new FeedbackRepository(reader).GetAsync(1, 2);
        Assert.Equal(new FeedbackDto(newestFeedback.Id, authorId, string.Empty,
            newestFeedback.Content, newestFeedback.CreatedAtUtc), feedback.Feedback[0]);
        Assert.Equal(seed.Login, feedback.Feedback[1].AuthorLogin);
        Assert.Empty(capture.Entities);
        Assert.Empty(reader.ChangeTracker.Entries());

        await using var cleanup = database.CreateContext();
        await cleanup.Feedback.ExecuteDeleteAsync();
        var empty = await new FeedbackRepository(reader).GetAsync(1, 50);
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Feedback);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadPages_CancelledToken_PropagatesCancellation(bool comments)
    {
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (comments) await new InteractionRepository(context).GetCommentsAsync(1, 1, 50, cancellation.Token);
            else await new FeedbackRepository(context).GetAsync(1, 50, cancellation.Token);
        });
    }

    [InteractionReadBenchmark]
    public async Task ReadPages_OptInComparison_ReportsSyntheticBeforeAndAfterEvidence()
    {
        var seed = await SeedAsync();
        await CompareAsync<CommentDto>("comments", async context =>
        {
            var query = context.Comments.AsNoTracking().Where(comment => comment.ImageId == seed.ImageId && comment.DeletedAtUtc == null);
            var total = await query.CountAsync();
            var entities = await query.Include(comment => comment.Author)
                .OrderBy(comment => comment.CreatedAtUtc).ThenBy(comment => comment.Id).Skip(0).Take(50).ToListAsync();
            return (entities.Select(comment => new CommentDto(comment.Id, comment.ImageId, comment.AuthorId,
                comment.Author?.Login ?? string.Empty, comment.Content, comment.CreatedAtUtc)).ToArray(), total);
        }, async context =>
        {
            var result = await new InteractionRepository(context).GetCommentsAsync(seed.ImageId, 1, 50);
            return (result.Comments, result.Total);
        });
        await CompareAsync<FeedbackDto>("feedback", async context =>
        {
            var query = context.Feedback.AsNoTracking();
            var total = await query.CountAsync();
            var entities = await query.Include(item => item.Author)
                .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Skip(0).Take(50).ToListAsync();
            return (entities.Select(item => new FeedbackDto(item.Id, item.AuthorId,
                item.Author?.Login ?? string.Empty, item.Content, item.CreatedAtUtc)).ToArray(), total);
        }, async context =>
        {
            var result = await new FeedbackRepository(context).GetAsync(1, 50);
            return (result.Feedback, result.Total);
        });
    }

    private async Task CompareAsync<T>(string name,
        Func<RepositoryContext, Task<(IReadOnlyList<T> Items, int Total)>> before,
        Func<RepositoryContext, Task<(IReadOnlyList<T> Items, int Total)>> after)
    {
        var oldCapture = new InteractionReadCapture();
        var newCapture = new InteractionReadCapture();
        await using (var oldContext = database.CreateContext(oldCapture))
        await using (var newContext = database.CreateContext(newCapture))
        {
            var oldResult = await before(oldContext);
            var newResult = await after(newContext);
            Assert.Equal(oldResult.Total, newResult.Total);
            Assert.Equal(oldResult.Items, newResult.Items);
            Assert.Equal(50, newResult.Items.Count);
        }
        output.WriteLine($"{name}: page=50; two SQL reads each; entities {oldCapture.Entities.Count} -> {newCapture.Entities.Count}; selected page columns {oldCapture.SelectedColumns[1].Length} -> {newCapture.SelectedColumns[1].Length}");
        output.WriteLine($"{name} old columns: {string.Join(", ", oldCapture.SelectedColumns[1])}");
        output.WriteLine($"{name} new columns: {string.Join(", ", newCapture.SelectedColumns[1])}");
        Assert.Empty(newCapture.Entities);
        Assert.Equal(100, oldCapture.Entities.Count);
        Assert.Equal(2, oldCapture.SelectedColumns.Count);
        Assert.Equal(2, newCapture.SelectedColumns.Count);

        // Timing has no interceptors; whole-process allocation counts suit async continuations.
        // Run this opt-in test alone. Results describe synthetic local PostgreSQL, not production.
        await using var oldReader = database.CreateContext();
        await using var newReader = database.CreateContext();
        for (var index = 0; index < 12; index++)
        {
            await before(oldReader);
            await after(newReader);
        }
        var oldSamples = new List<(double Milliseconds, long Bytes)>();
        var newSamples = new List<(double Milliseconds, long Bytes)>();
        for (var index = 0; index < 40; index++)
        {
            if (index % 2 == 0)
            {
                oldSamples.Add(await MeasureAsync(() => before(oldReader)));
                newSamples.Add(await MeasureAsync(() => after(newReader)));
            }
            else
            {
                newSamples.Add(await MeasureAsync(() => after(newReader)));
                oldSamples.Add(await MeasureAsync(() => before(oldReader)));
            }
        }
        Report("before", oldSamples);
        Report("after", newSamples);

        void Report(string version, List<(double Milliseconds, long Bytes)> samples)
        {
            var times = samples.Select(sample => sample.Milliseconds).Order().ToArray();
            var bytes = samples.Select(sample => sample.Bytes).Order().ToArray();
            output.WriteLine($"{name} {version}: n=40; ms min/median/max={times[0]:F3}/{(times[19] + times[20]) / 2:F3}/{times[^1]:F3}; allocated bytes min/median/max={bytes[0]}/{(bytes[19] + bytes[20]) / 2}/{bytes[^1]}");
        }
    }

    private static async Task<(double Milliseconds, long Bytes)> MeasureAsync<T>(Func<Task<T>> read)
    {
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var start = Stopwatch.GetTimestamp();
        await read();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (elapsed, GC.GetTotalAllocatedBytes(precise: true) - allocated);
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = database.CreateContext();
        await context.Feedback.ExecuteDeleteAsync();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var author = new AppUser
        {
            Login = "reader-" + Guid.NewGuid().ToString("N"), NormalizedLogin = Guid.NewGuid().ToString("N"),
            PasswordHash = new string('x', 256), CreatedAtUtc = now,
            AuthenticatorSecret = new string('s', 256), PendingAuthenticatorSecret = new string('p', 256),
            AuthenticatorBackupCodeHashes = Enumerable.Repeat(new string('b', 64), 8).ToList()
        };
        context.AppUsers.Add(author);
        await context.SaveChangesAsync();
        var images = Enumerable.Range(0, 2).Select(_ => new UserMadeImage
        {
            Source = ImageSource.UserUpload, UploadedById = author.Id, UploadedAtUtc = now,
            Visibility = ImageVisibility.Gallery, ModerationStatus = ModerationStatus.Approved,
            StorageKey = "synthetic/" + Guid.NewGuid().ToString("N"), ContentType = "image/jpeg",
            Width = 100, Height = 80
        }).ToArray();
        var comments = Enumerable.Range(0, 120).Select(index => new Comment
        {
            AuthorId = author.Id, Content = "comment " + index, CreatedAtUtc = now.AddMinutes(index % 4),
            DeletedAtUtc = index % 12 == 0 ? now : null
        }).ToArray();
        images[0].Comments = comments.ToList();
        images[1].Comments = [new Comment { AuthorId = author.Id, Content = "other image", CreatedAtUtc = now }];
        var feedback = Enumerable.Range(0, 120).Select(index => new Feedback
        {
            AuthorId = author.Id, Content = "feedback " + index, CreatedAtUtc = now.AddMinutes(index % 4)
        }).ToArray();
        context.Images.AddRange(images.AsEnumerable());
        context.Feedback.AddRange(feedback);
        await context.SaveChangesAsync();
        return new(images[0].Id, author.Login, comments, feedback);
    }

    private sealed record Seed(int ImageId, string Login, Comment[] Comments, Feedback[] Feedback);
}
