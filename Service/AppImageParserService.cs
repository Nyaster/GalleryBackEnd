using System.Globalization;
using System.Net;
using System.Diagnostics;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Service.Contracts;
using Configuration = AngleSharp.Configuration;

namespace Service;

public sealed class AppImageParserService(
    IRepositoryManager repositories,
    IImageStorage storage,
    IImageProcessor processor,
    IOptions<ParserSettings> options,
    TimeProvider clock,
    ILogger<AppImageParserService> logger) : IImageParserService
{
    public async Task<ScrapeResult> RunAsync(ScrapeRun run, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new InvalidOperationException("Scraping is disabled by configuration.");
        if (string.IsNullOrWhiteSpace(settings.ParserLogin) || string.IsNullOrWhiteSpace(settings.ParserPassword))
            throw new InvalidOperationException("Parser credentials are not configured.");

        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader().WithDefaultCookies());
        logger.LogInformation("Scraper authentication started");
        try
        {
            await EnsureLoginAsync(context, settings, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Scraper authentication failed {FailureType}", exception.GetType().Name);
            throw;
        }
        logger.LogInformation("Scraper authentication succeeded");
        var firstPage = await context.OpenAsync(settings.RequestsUrl, cancellationToken);
        var pageCount = run.Mode == ScrapeMode.Full ? GetPageCount(firstPage) : settings.IncrementalPages;
        var candidates = new List<ScrapedCandidate>();
        var mediaIds = new HashSet<int>();
        run.TotalPages = Math.Max(pageCount, 1);
        for (var page = 1; page <= Math.Max(pageCount, 1); page++)
        {
            var document = page == 1 ? firstPage : await context.OpenAsync($"{settings.RequestsUrl}?page={page}", cancellationToken);
            var pageCandidates = ExtractCandidates(document, settings.SiteUrl).ToList();
            foreach (var candidate in pageCandidates)
                if (mediaIds.Add(candidate.MediaId)) candidates.Add(candidate);

            run.ScannedPages = page;
            run.ImagesDiscovered = candidates.Count;
            logger.LogInformation("Scrape source page scanned {PageNumber} {TotalPages} {PageCandidates} {DiscoveredCandidates}",
                page, run.TotalPages, pageCandidates.Count, run.ImagesDiscovered);
            await SaveProgressAsync(run, "source-page", cancellationToken);
        }
        var existing = await repositories.AppImage.GetByExternalMediaIdsAsync(candidates.Select(candidate => candidate.MediaId), true, cancellationToken);
        await SynchronizeExistingImagesAsync(existing, candidates, cancellationToken);
        var existingIds = existing.Select(image => image.ExternalMediaId!.Value).ToHashSet();
        var newCandidates = candidates.Where(candidate => !existingIds.Contains(candidate.MediaId)).ToList();
        var plannedCandidates = newCandidates.Take(run.MaxImages).ToList();
        run.EligibleCandidates = newCandidates.Count;
        run.PlannedDownloads = plannedCandidates.Count;
        logger.LogInformation("Scrape candidate selection completed {DiscoveredCandidates} {EligibleCandidates} {PlannedDownloads} {ExistingCandidates}",
            run.ImagesDiscovered, run.EligibleCandidates, run.PlannedDownloads, existingIds.Count);
        await SaveProgressAsync(run, "candidate-selection", cancellationToken);
        var cookie = context.GetCookie(new Url(settings.SiteUrl));

        for (var index = 0; index < plannedCandidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = plannedCandidates[index];
            var elapsed = Stopwatch.StartNew();
            logger.LogInformation("Scrape image import started {MediaId} {Host} {Path} {Ordinal}",
                candidate.MediaId, candidate.ImageUrl.Host, candidate.ImageUrl.AbsolutePath, index + 1);
            try
            {
                if (await ImportCandidateAsync(candidate, cookie, settings, cancellationToken)) run.ImagesImported++;
                logger.LogInformation("Scrape image import completed {MediaId} {Host} {Path} {Ordinal} {ElapsedMilliseconds}",
                    candidate.MediaId, candidate.ImageUrl.Host, candidate.ImageUrl.AbsolutePath, index + 1,
                    Math.Round(elapsed.Elapsed.TotalMilliseconds, 2));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                run.FailedItems++;
                run.CompletedWithErrors = true;
                logger.LogWarning("Scrape image import failed {MediaId} {Host} {Path} {Ordinal} {ElapsedMilliseconds} {FailureType}",
                    candidate.MediaId, candidate.ImageUrl.Host, candidate.ImageUrl.AbsolutePath, index + 1,
                    Math.Round(elapsed.Elapsed.TotalMilliseconds, 2), exception.GetType().Name);
            }
            finally
            {
                run.ProcessedDownloads++;
                await SaveProgressAsync(run, "download-attempt", CancellationToken.None);
            }
        }
        return new ScrapeResult(run.ImagesDiscovered, run.ImagesImported, run.CompletedWithErrors, run.FailedItems);
    }

    private async Task SynchronizeExistingImagesAsync(IReadOnlyList<AppImage> existingImages,
        IReadOnlyList<ScrapedCandidate> candidates, CancellationToken cancellationToken)
    {
        if (existingImages.Count == 0) return;

        var candidatesByMediaId = candidates.ToDictionary(candidate => candidate.MediaId);
        var sourceTagsByMediaId = candidatesByMediaId.ToDictionary(pair => pair.Key,
            pair => NormalizeTags(pair.Value.Tags));
        var tagsByName = new Dictionary<string, ImageTag>(StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        foreach (var tagNames in sourceTagsByMediaId.Values.SelectMany(tags => tags).Distinct().Chunk(20))
            foreach (var tag in await repositories.AppImage.GetOrCreateTagsAsync(tagNames, now, cancellationToken))
                tagsByName[tag.NormalizedName] = tag;

        var changedImages = 0;
        foreach (var image in existingImages)
        {
            var source = candidatesByMediaId[image.ExternalMediaId!.Value];
            var sourceTags = sourceTagsByMediaId[source.MediaId]
                .Select(tagName => tagsByName[tagName]).ToList();
            var tagsChanged = !HaveSameTags(image.Tags, sourceTags);
            var uploadedAtChanged = image.UploadedAtUtc != source.UploadedAtUtc;
            if (!tagsChanged && !uploadedAtChanged) continue;

            image.Tags = sourceTags;
            image.UploadedAtUtc = source.UploadedAtUtc;
            changedImages++;
        }

        if (changedImages > 0)
            logger.LogInformation("Synchronized source metadata for {UpdatedImages} existing scraped images", changedImages);
    }

    private static List<string> NormalizeTags(IReadOnlyList<string> tags)
        => tags.Select(tag => tag.Trim().ToLowerInvariant()).Where(tag => tag.Length > 0).Distinct().Take(20).ToList();

    private static bool HaveSameTags(IReadOnlyList<ImageTag> currentTags, IReadOnlyList<ImageTag> sourceTags)
        => currentTags.Select(tag => tag.NormalizedName).Order(StringComparer.Ordinal)
            .SequenceEqual(sourceTags.Select(tag => tag.NormalizedName).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private async Task SaveProgressAsync(ScrapeRun run, string checkpoint, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        run.LastProgressAtUtc = now;
        run.LeaseExpiresAtUtc = now.AddMinutes(30);
        await repositories.SaveAsync(cancellationToken);
        logger.LogInformation("Scrape progress checkpoint {Checkpoint} {ScannedPages} {TotalPages} {DiscoveredCandidates} {EligibleCandidates} {PlannedDownloads} {ProcessedDownloads} {ImagesImported} {FailedItems}",
            checkpoint, run.ScannedPages, run.TotalPages, run.ImagesDiscovered, run.EligibleCandidates,
            run.PlannedDownloads, run.ProcessedDownloads, run.ImagesImported, run.FailedItems);
    }

    private async Task<bool> ImportCandidateAsync(ScrapedCandidate candidate, string cookie, ParserSettings settings,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        string? storageKey = null;
        try
        {
            EnsureAllowedImageUri(candidate.ImageUrl, settings);
            using var handler = new HttpClientHandler { CookieContainer = CreateCookies(cookie, new Uri(settings.SiteUrl).Host), AllowAutoRedirect = false };
            using var client = new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(30);
            using var response = await SendImageRequestAsync(client, candidate.ImageUrl, settings, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > settings.MaximumDownloadMegabytes * 1024L * 1024L)
                throw new ImageUploadValidationError("Remote image exceeds the configured download limit.");
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var boundedBody = new LengthLimitedReadStream(body, settings.MaximumDownloadMegabytes * 1024L * 1024L);
            temporaryPath = await storage.SaveTemporaryAsync(boundedBody, cancellationToken);
            var inspected = await processor.InspectAsync(temporaryPath, cancellationToken);
            storageKey = $"scraped/{candidate.MediaId}-{Guid.NewGuid():N}{inspected.Extension}";
            await storage.MoveTemporaryToFinalAsync(temporaryPath, storageKey, cancellationToken);
            temporaryPath = null;
            var tags = await repositories.AppImage.GetOrCreateTagsAsync(candidate.Tags, clock.GetUtcNow(), cancellationToken);
            await repositories.AppImage.AddAsync(new SelebusImage
            {
                Source = ImageSource.Scraped,
                ExternalMediaId = candidate.MediaId,
                UploadedAtUtc = candidate.UploadedAtUtc,
                Visibility = ImageVisibility.Gallery,
                ModerationStatus = ModerationStatus.Approved,
                AiUsage = AiUsageClassification.HumanMade,
                StorageKey = storageKey,
                ContentType = inspected.ContentType,
                Width = inspected.Width,
                Height = inspected.Height,
                Tags = tags,
                EmbeddingStatus = EmbeddingStatus.Pending
            }, cancellationToken);
            await repositories.SaveAsync(cancellationToken);
            return true;
        }
        catch
        {
            if (storageKey is not null)
                await storage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task EnsureLoginAsync(IBrowsingContext context, ParserSettings settings, CancellationToken cancellationToken)
    {
        using var loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loginCancellation.CancelAfter(TimeSpan.FromSeconds(settings.LoginTimeoutSeconds));

        try
        {
            var protectedPage = await context.OpenAsync(settings.RequestsUrl, loginCancellation.Token)
                .WaitAsync(loginCancellation.Token);
            if (IsRequestsPage(protectedPage, settings.RequestsUrl)) return;

            var loginPage = await context.OpenAsync(settings.LoginUrl, loginCancellation.Token)
                .WaitAsync(loginCancellation.Token);
            var form = loginPage.Forms.FirstOrDefault() ?? throw new InvalidOperationException("Parser login form was not found.");
            form.SetValues(new Dictionary<string, string>
            {
                ["loginModel.Username"] = settings.ParserLogin,
                ["loginModel.Password"] = settings.ParserPassword
            });

            // AngleSharp's form submission API does not accept a CancellationToken. WaitAsync still
            // bounds the worker, even if the underlying request ignores cancellation.
            await form.SubmitAsync().WaitAsync(loginCancellation.Token);

            protectedPage = await context.OpenAsync(settings.RequestsUrl, loginCancellation.Token)
                .WaitAsync(loginCancellation.Token);
            if (!IsRequestsPage(protectedPage, settings.RequestsUrl))
                throw new InvalidOperationException("Parser login was rejected or did not grant access to the requests gallery.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && loginCancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"Parser login did not complete within {settings.LoginTimeoutSeconds} seconds.");
        }
    }

    private static bool IsRequestsPage(IDocument document, string requestsUrl)
    {
        if (!Uri.TryCreate(requestsUrl, UriKind.Absolute, out var requestsUri) ||
            !Uri.TryCreate(document.Url, UriKind.Absolute, out var documentUri)) return false;

        return string.Equals(requestsUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            documentUri.GetLeftPart(UriPartial.Path).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPageCount(IDocument document)
    {
        var message = document.QuerySelector("div.message")?.TextContent ?? string.Empty;
        var digits = new string(message.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var count) ? Math.Max((int)Math.Ceiling(count / 20d), 1) : 1;
    }

    private IEnumerable<ScrapedCandidate> ExtractCandidates(IDocument document, string siteUrl)
    {
        var elapsed = Stopwatch.StartNew();
        var ordinal = 0;
        foreach (var element in document.QuerySelectorAll(".requests > .block:not(.hidden) > .block-inner"))
        {
            ordinal++;
            var imagePath = element.QuerySelector("img[src]")?.GetAttribute("src");
            var mediaIdText = element.QuerySelector("div.like")?.GetAttribute("data-media-id");
            var dateText = element.QuerySelector(".overlay>p:nth-child(1)")?.TextContent;
            if (string.IsNullOrWhiteSpace(imagePath) || !int.TryParse(mediaIdText, out var mediaId) || dateText is null)
            {
                LogMalformedSourceItem(mediaIdText, null, ordinal, elapsed, "Missing image, media ID, or date.");
                continue;
            }
            if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var siteUri) || !Uri.TryCreate(siteUri, imagePath, out var imageUri))
            {
                LogMalformedSourceItem(mediaIdText, null, ordinal, elapsed, "Image source is not a valid URI.");
                continue;
            }
            if (imageUri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                LogMalformedSourceItem(mediaIdText, imageUri, ordinal, elapsed, "GIF images are not imported.");
                continue;
            }
            var dateValue = dateText.Replace("Added:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!DateTimeOffset.TryParseExact(dateValue, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                LogMalformedSourceItem(mediaIdText, imageUri, ordinal, elapsed, "Added date is not valid.");
                continue;
            }
            var rawTags = element.QuerySelector(".overlay > p.tags")?.TextContent ?? string.Empty;
            var tags = rawTags.Replace("tags:", string.Empty, StringComparison.OrdinalIgnoreCase).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            yield return new ScrapedCandidate(mediaId, ToOriginalImageUri(imageUri), date.ToUniversalTime(), tags);
        }
    }

    private void LogMalformedSourceItem(string? mediaId, Uri? imageUri, int ordinal, Stopwatch elapsed, string reason)
        => logger.LogWarning("Skipping malformed scrape source item {MediaId} {Host} {Path} {Ordinal} {ElapsedMilliseconds} {Reason}",
            mediaId ?? "unknown", imageUri?.Host ?? "unknown", imageUri?.AbsolutePath ?? "/", ordinal,
            Math.Round(elapsed.Elapsed.TotalMilliseconds, 2), reason);

    private static Uri ToOriginalImageUri(Uri imageUri)
    {
        if (string.IsNullOrEmpty(imageUri.Query)) return imageUri;

        var retainedParameters = imageUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var separator = parameter.IndexOf('=');
                var encodedName = separator < 0 ? parameter : parameter[..separator];
                var name = Uri.UnescapeDataString(encodedName);
                return !string.Equals(name, "width", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "height", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "format", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "quality", StringComparison.OrdinalIgnoreCase);
            });
        var builder = new UriBuilder(imageUri) { Query = string.Join('&', retainedParameters) };
        return builder.Uri;
    }

    private static CookieContainer CreateCookies(string rawCookies, string host)
    {
        var container = new CookieContainer();
        foreach (var part in rawCookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2) container.Add(new Cookie(pair[0], pair[1], "/", host));
        }
        return container;
    }

    private static void EnsureAllowedImageUri(Uri uri, ParserSettings settings)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scraped images must use HTTPS.");
        var allowed = settings.AllowedImageHosts.Length == 0 ? [new Uri(settings.SiteUrl).Host] : settings.AllowedImageHosts;
        if (!allowed.Any(host => string.Equals(host.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Scraped image host is not allowed.");
    }

    private static async Task<HttpResponseMessage> SendImageRequestAsync(HttpClient client, Uri initialUri, ParserSettings settings, CancellationToken cancellationToken)
    {
        var uri = initialUri;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            EnsureAllowedImageUri(uri, settings);
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!((int)response.StatusCode is >= 300 and < 400)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidOperationException("Remote image redirect has no location.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidOperationException("Remote image exceeded redirect limit.");
    }

    private sealed class LengthLimitedReadStream(Stream inner, long maximumLength) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            _read += count;
            if (_read > maximumLength) throw new ImageUploadValidationError("Remote image exceeds the configured download limit.");
            return count;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed record ScrapedCandidate(int MediaId, Uri ImageUrl, DateTimeOffset UploadedAtUtc, IReadOnlyList<string> Tags);
}
