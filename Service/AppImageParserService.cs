using System.Globalization;
using System.Net;
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
    public async Task<ScrapeResult> RunAsync(ScrapeMode mode, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new InvalidOperationException("Scraping is disabled by configuration.");
        if (string.IsNullOrWhiteSpace(settings.ParserLogin) || string.IsNullOrWhiteSpace(settings.ParserPassword))
            throw new InvalidOperationException("Parser credentials are not configured.");

        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader().WithDefaultCookies());
        logger.LogInformation("Authenticating scraper with the source site");
        await EnsureLoginAsync(context, settings, cancellationToken);
        logger.LogInformation("Scraper authentication succeeded");
        var firstPage = await context.OpenAsync(settings.RequestsUrl, cancellationToken);
        var pageCount = mode == ScrapeMode.Full ? GetPageCount(firstPage) : settings.IncrementalPages;
        var candidates = new List<ScrapedCandidate>();
        for (var page = 1; page <= Math.Max(pageCount, 1); page++)
        {
            var document = page == 1 ? firstPage : await context.OpenAsync($"{settings.RequestsUrl}?page={page}", cancellationToken);
            candidates.AddRange(ExtractCandidates(document, settings.SiteUrl));
        }
        candidates = candidates.DistinctBy(candidate => candidate.MediaId).ToList();
        var existing = await repositories.AppImage.GetByExternalMediaIdsAsync(candidates.Select(candidate => candidate.MediaId), false, cancellationToken);
        var existingIds = existing.Select(image => image.ExternalMediaId!.Value).ToHashSet();
        var newCandidates = candidates.Where(candidate => !existingIds.Contains(candidate.MediaId)).ToList();
        var imported = 0;
        var failed = 0;
        var cookie = context.GetCookie(new Url(settings.SiteUrl));

        foreach (var candidate in newCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ImportCandidateAsync(candidate, cookie, settings, cancellationToken)) imported++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(exception, "Skipping malformed or unavailable scraped image {MediaId}", candidate.MediaId);
            }
        }
        return new ScrapeResult(candidates.Count, imported, failed > 0, failed);
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

    private static IEnumerable<ScrapedCandidate> ExtractCandidates(IDocument document, string siteUrl)
    {
        foreach (var element in document.QuerySelectorAll(".requests > .block:not(.hidden) > .block-inner"))
        {
            var imagePath = element.QuerySelector("a")?.GetAttribute("href");
            var mediaIdText = element.QuerySelector("div.like")?.GetAttribute("data-media-id");
            var dateText = element.QuerySelector(".overlay>p:nth-child(1)")?.TextContent;
            if (string.IsNullOrWhiteSpace(imagePath) || !int.TryParse(mediaIdText, out var mediaId) || dateText is null)
                continue;
            if (!Uri.TryCreate(new Uri(siteUrl), imagePath, out var imageUri) || imageUri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                continue;
            var dateValue = dateText.Replace("Added:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!DateTimeOffset.TryParseExact(dateValue, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                continue;
            var rawTags = element.QuerySelector(".overlay > p.tags")?.TextContent ?? string.Empty;
            var tags = rawTags.Replace("tags:", string.Empty, StringComparison.OrdinalIgnoreCase).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            yield return new ScrapedCandidate(mediaId, imageUri, date.ToUniversalTime(), tags);
        }
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
