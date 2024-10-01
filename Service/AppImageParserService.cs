using System.Globalization;
using System.Net;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Contracts;
using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.IdentityModel.Tokens;
using Service.Contracts;
using Service.Helpers;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Service;

public class AppImageParserService(IRepositoryManager repositoryManager, IConfiguration configuration)
    : IImageParserService
{
    private const string SiteUrl = "https://lessonsinlovegame.com";
    private const string LoginUrl = "https://lessonsinlovegame.com/account/login/";
    private const string RequestsUrl = "https://lessonsinlovegame.com/galleries/requests";
    private const string GravureSetsUrl = "https://lessonsinlovegame.com/galleries/gravure-sets";
    private const string CookiesFilePath = ".configs/cookies.json";
    private const int DefaultPageSize = 20;
    private const int DefaultCheckUpdatesPages = 2;


    private async Task<IBrowsingContext> PrepareForScrappingAsync()
    {
        var config = Configuration.Default
            .WithDefaultLoader().WithDefaultCookies();
        var context = BrowsingContext.New(config);
        await CheckAndLoadCookies(context);
        await CheckIfSuccessfulLogin(context);
        return context;
    }

    public async Task CheckUpdates()
    {
        var context = await PrepareForScrappingAsync();
        await ExtractAndHandleContent(context, DefaultCheckUpdatesPages);
    }

    public async Task DownloadImages()
    {
        var context = await PrepareForScrappingAsync();
        var document = await context.OpenAsync(RequestsUrl);
        var numberOfPagesToScrap = GetNumberOfPages(document);
        await ExtractAndHandleContent(context, numberOfPagesToScrap);
    }
    private async Task ExtractAndHandleContent(IBrowsingContext context, int numberOfPagesToScrap)
    {
        var extractedImages = await ExtractImagesAndTagsAsync(context, numberOfPagesToScrap, RequestsUrl);
        var imagesInDb = await repositoryManager.AppImage.FindImageByMediaId(extractedImages, false);
        var newImages = extractedImages.ExceptBy(imagesInDb.Select(x => x.MediaId), y => y.MediaId).ToList();
        if (newImages.Any()) await ProcessNewImages(context, newImages);

        await CheckAndHandleNewTagsOnImages(extractedImages, imagesInDb);
        var removeNoneYet = await repositoryManager.AppImage.FindImageByMediaId(extractedImages, true);
        var tagToDelete = removeNoneYet.FirstOrDefault(x => x.Tags.Count > 1).Tags
            .FirstOrDefault(x => x.Name == "none yet");
        removeNoneYet.FindAll(x => x.Tags.Any(y => y.Name == "none yet") && x.Tags.Count != 1).ToList()
            .ForEach(x => x.Tags.Remove(tagToDelete));
        await repositoryManager.Save();
    }

    private async Task CheckAndHandleNewTagsOnImages(List<AppImage> extractedImages, List<AppImage> imagesInDb)
    {
        var imagesWithoutTags =
            imagesInDb.FindAll(x => x.Tags.Any(x => x.Name.ToLower().Trim() == "none yet") && x.Tags.Count == 1);
        extractedImages = extractedImages.IntersectBy(imagesWithoutTags.Select(x => x.MediaId), y => y.MediaId)
            .ToList();
        if (imagesWithoutTags.IsNullOrEmpty())
        {
            return;
        }

        var patchedImages = new List<AppImage>();
        foreach (var image in extractedImages)
        {
            var whereReplaceTags = imagesWithoutTags.First(x => x.MediaId == image.MediaId);

            if (whereReplaceTags.Tags.Count == image.Tags.Count &&
                image.Tags.Any(x => x.Name.ToLower().Trim() == "none yet"))
            {
                continue;
            }

            whereReplaceTags.Tags = image.Tags;
            patchedImages.Add(whereReplaceTags);
        }

        var distinctBy = patchedImages.SelectMany(x => x.Tags).DistinctBy(x => x.Name.Trim().ToLower()).ToList();
        var tagsFromDb = await SaveNewTagsToDb(distinctBy);
        ReplaceTagsInImagesFromDb(tagsFromDb, patchedImages);
        await repositoryManager.AppImage.UpdateImagesAsync(patchedImages);
        await repositoryManager.Save();
    }

    public async Task DownloadAllImages()
    {
        var context = await PrepareForScrappingAsync();
        var document = await context.OpenAsync(RequestsUrl);
        var numberOfPagesToScrap = GetNumberOfPages(document);
        var imagesAndTags = await ExtractImagesAndTagsAsync(context, numberOfPagesToScrap, RequestsUrl);
        var imagesInDb = await repositoryManager.AppImage.FindImageByMediaId(imagesAndTags, true);
        var newImages = imagesAndTags.ExceptBy(imagesInDb.Select(x => x.MediaId), y => y.MediaId).ToList();
        if (newImages.Any()) await ProcessNewImages(context, newImages);
    }

    private async Task ProcessNewImages(IBrowsingContext context, List<AppImage?> newImages)
    {
        var distinctBy = newImages.SelectMany(x => x.Tags).DistinctBy(x => x.Name).ToList();
        var tagsFromDb = await SaveNewTagsToDb(distinctBy);
        repositoryManager.AppImage.AttachTags(tagsFromDb);
        ReplaceTagsInImagesFromDb(tagsFromDb, newImages);

        var images = await DownloadImagesAsync(context, newImages);
        var byLoginAsync = await repositoryManager.AppUser.GetByLoginAsync("admin", false);
        images.AsParallel().ForAll(x =>
        {
            var (width, height) = ImageHelpers.GetImageDimensions(x.PathToFileOnDisc);
            x.Width = width;
            x.Height = height;
            x.UploadedById = byLoginAsync!.Id;
        });
        await repositoryManager.AppImage.AddImagesAsync(newImages);
        await repositoryManager.Save();
    }

    private static void ReplaceTagsInImagesFromDb(List<ImageTag> tagsFromDb, List<AppImage?> appImages)
    {
        var tagDictionary =
            tagsFromDb.ToDictionary(t => t.Name, t => t); // Create a dictionary for quick lookup by name

        foreach (var appImage in appImages)
            for (var i = 0; i < appImage.Tags.Count; i++)
            {
                var tagName = appImage.Tags[i].Name;

                if (tagDictionary.TryGetValue(tagName, out var replacementTag))
                    appImage.Tags[i] = replacementTag; // Replace with the tag from tagsFromDb
            }
    }

    private async Task<List<ImageTag>> SaveNewTagsToDb(List<ImageTag> distinctBy)
    {
        var uniqueTags = distinctBy.Select(x => x.Name).ToList();
        var tagsFromDb =
            await repositoryManager.AppImage.GetExistingTagsFromDb(uniqueTags);
        var newTags = distinctBy.ExceptBy(tagsFromDb.Select(x => x.Name), x => x.Name).ToList();

        await repositoryManager.AppImage.AddTags(newTags);
        await repositoryManager.Save();
        tagsFromDb.AddRange(newTags);
        return tagsFromDb;
    }

    private async Task CheckIfSuccessfulLogin(IBrowsingContext context)
    {
        var loggedIn = await context.OpenAsync(RequestsUrl);
        if (loggedIn.Url != RequestsUrl)
        {
            await LoginAsync(context);
            await SaveCookiesAsync(context);
        }
    }

    private async Task CheckAndLoadCookies(IBrowsingContext context)
    {
        if (File.Exists(CookiesFilePath)) await LoadCookiesAsync(context);
    }

    private static int GetNumberOfPages(IDocument page)
    {
        var numberOfPagesInString = page.QuerySelector("div.message");
        var textNumberOfPages = numberOfPagesInString!.Text();
        var numberOfItems = int.Parse(string.Concat(textNumberOfPages.Where(char.IsDigit)));
        var numberOfPages = (int)Math.Ceiling((double)numberOfItems / DefaultPageSize);
        return numberOfPages;
    }

    private async Task<List<AppImage>> DownloadImagesAsync(IBrowsingContext page,
        List<AppImage> imagesAndTags)
    {
        var pathToDirectory = Path.Combine(Directory.GetCurrentDirectory(), "upload", "images", "selebus");
        Directory.CreateDirectory(pathToDirectory);
        var downloadTasks = new List<Task<AppImage>>();
        var cookies = page.GetCookie(new Url(SiteUrl));
        var semaphore = new SemaphoreSlim(3);
        foreach (var appImage in imagesAndTags)
            // Start a new task and add it to the list
            downloadTasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(); // Wait for the semaphore to be available
                try
                {
                    return await DownloadImageAsync(appImage, cookies);
                }
                finally
                {
                    semaphore.Release(); // Release the semaphore once the task is done
                }
            }));

        var results = await Task.WhenAll(downloadTasks);

        return results.ToList();
    }


    private async Task<AppImage> DownloadImageAsync(AppImage appImage, string cookie)
    {
        var fullImageUrl = SiteUrl + appImage.PathToFileOnDisc;
        var fileName = Path.GetFileName(new Uri(fullImageUrl).LocalPath);
        var pathToDirectory = Path.Combine(Directory.GetCurrentDirectory(), "upload", "images", "selebus");
        var filePath = Path.Combine(pathToDirectory, appImage.MediaId + fileName);
        var cookiesPairs = cookie.Split(";", StringSplitOptions.RemoveEmptyEntries);

        if (File.Exists(filePath))
        {
            Console.WriteLine($"Image {fileName} already exists on disk. Skipping download.");
            appImage.PathToFileOnDisc = filePath;
            return appImage;
        }

        Console.WriteLine($"Downloading {fileName}...");


        // Set cookies in the HttpClient if needed
        var handler = new HttpClientHandler();
        var cookieContainer = new CookieContainer();
        foreach (var cookiePair in cookiesPairs)
        {
            var cookieParts = cookiePair.Split('=', StringSplitOptions.TrimEntries);
            if (cookieParts.Length == 2)
            {
                var name = cookieParts[0].Trim();
                var value = cookieParts[1].Trim();
                cookieContainer.Add(new Cookie(name, value, "/",
                    "lessonsinlovegame.com"));
            }
        }


        handler.CookieContainer = cookieContainer;

        using var httpClient = new HttpClient(handler);

        try
        {
            using var response =
                await httpClient.GetAsync(fullImageUrl.Split('?')[0], HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fileStream);

            Console.WriteLine($"Downloaded {fileName} with tags:");
            appImage.PathToFileOnDisc = filePath;
            return appImage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download {fileName}: {ex.Message}");
            throw;
        }
    }

    private async Task<List<AppImage?>> ExtractImagesAndTagsAsync(IBrowsingContext page,
        int numberOfPages, string requestUrl)
    {
        var appImages = new List<AppImage?>();
        for (var i = 1; i <= numberOfPages; i++)
        {
            Console.WriteLine($"Parsing page {i} of {numberOfPages}");
            var openAsync = await page.OpenAsync($"{requestUrl}?page={i}");
            var elements = openAsync.QuerySelectorAll(".requests > .block:not(.hidden) > .block-inner");


            foreach (var element in elements)
            {
                var appImage = await ExtractImageDataFromElement(element);
                if (appImage == null) continue;

                appImages.Add(appImage);
            }
        }

        return appImages;
    }

    private static async Task<AppImage?> ExtractImageDataFromElement(IElement element)
    {
        var imageUrl = element.QuerySelector("a");
        var imageSrc = imageUrl!.GetAttribute("href");
        var tagsElement = element.QuerySelector(".overlay > p.tags");

        var mediaIdElement = element.QuerySelector("div.like");
        var mediaIdAttribute = mediaIdElement!.GetAttribute("data-media-id");
        var mediaId = int.Parse(mediaIdAttribute!);
        var dateElement = element.QuerySelector(".overlay>p:nth-child(1)");
        var dateText = dateElement.Text();
        var date = await GetDateFromString(dateText);
        var tagsText = tagsElement != null ? tagsElement.TextContent : string.Empty;
        tagsText = tagsText.ToLower().Replace("tags:", "");
        var fullImageUrl = SiteUrl + imageSrc;
        if (Path.GetExtension(new Uri(fullImageUrl).LocalPath)
            .Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return null;

        var tags = tagsText.ToLower().Split(",").Select(x => new ImageTag
        {
            CreatedById = 1,
            IsDeleted = false,
            Name = x.Trim(),
            CreatDateTime = DateTime.Now.ToUniversalTime()
        }).ToList();
        var appImage = new AppImage
        {
            MediaId = mediaId,
            Tags = tags,
            IsDeleted = false,
            IsHidden = false,
            PathToFileOnDisc = imageSrc,
            UploadedDate = date.ToUniversalTime()
        };
        return appImage;
    }

    private static Task<DateTime> GetDateFromString(string dateString)
    {
        var clearedInputString = dateString.Replace("Added: ", "").Trim();
        var format = "M/d/yyyy";
        return Task.FromResult(DateTime.ParseExact(clearedInputString, format, CultureInfo.InvariantCulture));
    }


    private async Task LoginAsync(IBrowsingContext page)
    {
        var openAsync = await page.OpenAsync(LoginUrl);
        var htmlFormElement = openAsync.Forms.FirstOrDefault();
        var dictionary = new Dictionary<string, string>
        {
            { "loginModel.Username", configuration["ParserLogin"] },
            { "loginModel.Password", configuration["ParserPassword"] }
        };
        var formElement = htmlFormElement.SetValues(dictionary);
        var submitAsync = await formElement.SubmitAsync();
    }


    private static async Task SaveCookiesAsync(IBrowsingContext page)
    {
        Console.WriteLine($"Cookies saved. {CookiesFilePath}");
        var cookies = page.GetCookie(new Url(SiteUrl));
        var json = JsonSerializer.Serialize(cookies);
        await File.WriteAllTextAsync(CookiesFilePath, json);
        Console.WriteLine("Cookies saved.");
    }

    private static async Task LoadCookiesAsync(IBrowsingContext page)
    {
        Console.WriteLine($"Cookies try load loaded. {CookiesFilePath}");
        var cookiesJson = await File.ReadAllTextAsync(CookiesFilePath);
        var cookies = JsonSerializer.Deserialize<string>(cookiesJson);
        var strings = cookies.Split(";", StringSplitOptions.RemoveEmptyEntries);
        foreach (var se in strings) page.SetCookie(new Url(SiteUrl), se);

        Console.WriteLine($"Cookies loaded. {page.GetCookie(new Url(SiteUrl))}");
    }
}