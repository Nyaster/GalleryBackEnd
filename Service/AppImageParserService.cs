using System.Globalization;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Contracts;
using Entities.Models;
using Microsoft.Extensions.Options;
using Service.Contracts;
using Configuration = AngleSharp.Configuration;

namespace Service;

public sealed class AppImageParserService(
    IRepositoryManager repositories,
    IImageStorage storage,
    IImageProcessor processor,
    IOptions<ParserSettings> options,
    TimeProvider clock) : IImageParserService
{
    public async Task<ScrapeResult> RunAsync(ScrapeMode mode, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new InvalidOperationException("Scraping is disabled by configuration.");
        if (string.IsNullOrWhiteSpace(settings.ParserLogin) || string.IsNullOrWhiteSpace(settings.ParserPassword))
            throw new InvalidOperationException("Parser credentials are not configured.");

        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader().WithDefaultCookies());
        await EnsureLoginAsync(context, settings, cancellationToken);
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
        var cookie = context.GetCookie(new Url(settings.SiteUrl));

        foreach (var candidate in newCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ImportCandidateAsync(candidate, cookie, settings, cancellationToken))
                imported++;
        }
        return new ScrapeResult(candidates.Count, imported);
    }

    private async Task<bool> ImportCandidateAsync(ScrapedCandidate candidate, string cookie, ParserSettings settings,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        string? storageKey = null;
        try
        {
            using var handler = new HttpClientHandler { CookieContainer = CreateCookies(cookie, new Uri(settings.SiteUrl).Host) };
            using var client = new HttpClient(handler, disposeHandler: false);
            client.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate.ImageUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            temporaryPath = await storage.SaveTemporaryAsync(body, cancellationToken);
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
        var protectedPage = await context.OpenAsync(settings.RequestsUrl, cancellationToken);
        if (protectedPage.Url.StartsWith(settings.RequestsUrl, StringComparison.OrdinalIgnoreCase)) return;
        var loginPage = await context.OpenAsync(settings.LoginUrl, cancellationToken);
        var form = loginPage.Forms.FirstOrDefault() ?? throw new InvalidOperationException("Parser login form was not found.");
        form.SetValues(new Dictionary<string, string>
        {
            ["loginModel.Username"] = settings.ParserLogin,
            ["loginModel.Password"] = settings.ParserPassword
        });
        await form.SubmitAsync(cancellationToken);
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

    private sealed record ScrapedCandidate(int MediaId, Uri ImageUrl, DateTimeOffset UploadedAtUtc, IReadOnlyList<string> Tags);
}
