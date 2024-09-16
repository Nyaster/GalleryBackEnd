using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Contracts;
using Entities.Models;
using GallerySiteBackend.Models;
using Microsoft.Extensions.Configuration;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Service;

public class AppImageParserService
{
    private const string SiteUrl = "https://lessonsinlovegame.com";
    private const string LoginUrl = "https://lessonsinlovegame.com/account/login/";
    private const string RequestsUrl = "https://lessonsinlovegame.com/galleries/requests";
    private const string CookiesFilePath = "cookies.json";
    private const int DefaultPageSize = 20;
    private IRepositoryManager _repositoryManager;
    private IConfiguration _configuration;
    public AppImageParserService(IRepositoryManager repositoryManager, IConfiguration configuration)
    {
        _repositoryManager = repositoryManager;
        _configuration = configuration;
    }

    public async Task CheckUpdates()
    {
        var config = Configuration.Default
            .WithDefaultLoader().WithDefaultCookies();
        IBrowsingContext context = BrowsingContext.New(config);


        if (File.Exists(CookiesFilePath))
        {
            var page = await context.OpenAsync(RequestsUrl);
            await LoadCookiesAsync(context);
        }

        var loggedIn = await context.OpenAsync(RequestsUrl);
        if (loggedIn.Url != RequestsUrl)
        {
            await LoginAsync(context);
            await SaveCookiesAsync(context);
        }

        var document = await context.OpenAsync(RequestsUrl);
        var numberOfPages = await GetNumberOfPages(document);
        var imagesAndTags = await ExtractImagesAndTagsAsync(context, numberOfPages);
        var imagesInDb = await _repositoryManager.AppImage.FindImageByMediaId(imagesAndTags);
        var appImages = imagesAndTags.ExceptBy(imagesInDb.Select(x => x.MediaId), x => x.MediaId).ToList();
        var distinctBy = appImages.SelectMany(x => x.Tags).DistinctBy(x => x.Name).ToList();
        var tagsFromDb =
            await _repositoryManager.AppImage.GetExistingTagsFromDb(distinctBy.Select(x => x.Name).ToList());
        var newTags = distinctBy.ExceptBy(tagsFromDb.Select(x => x.Name), x => x.Name).ToList();

        await _repositoryManager.AppImage.AddTags(newTags);
        await _repositoryManager.Save();
        tagsFromDb.AddRange(newTags);
        _repositoryManager.AppImage.AttachTags(tagsFromDb);
        var tagDictionary =
            tagsFromDb.ToDictionary(t => t.Name, t => t); // Create a dictionary for quick lookup by name

        foreach (var appImage in appImages)
        {
            for (int i = 0; i < appImage.Tags.Count; i++)
            {
                var tagName = appImage.Tags[i].Name;

                if (tagDictionary.TryGetValue(tagName, out var replacementTag))
                {
                    appImage.Tags[i] = replacementTag; // Replace with the tag from tagsFromDb
                }
            }
        }

        var images = await DownloadImagesAsync(context, appImages);
        var byLoginAsync = await _repositoryManager.AppUser.GetByLoginAsync("admin", trackChanges: false);
        images.AsParallel().ForAll(x =>
        {
            var (width, height) =  Helpers.ImageHelpers.GetImageDimensions(x.PathToFileOnDisc);
            x.Width = width;
            x.Height = height;
            x.UploadedById = byLoginAsync.Id;
        });
        await _repositoryManager.AppImage.AddImagesAsync(appImages);
        await _repositoryManager.Save();
        /*int batchSize = 100;
        int totalBatches = (int)Math.Ceiling((double)appImages.Count / batchSize);

        for (int i = 0; i < totalBatches; i++)
        {
            // Take a batch from the list
            var batch = appImages.Skip(i * batchSize).Take(batchSize).ToList();

            // Save the batch to the database
            await _repositoryManager.AppImage.AddImagesAsync(batch);
            await _repositoryManager.Save();
        }*/
    }

    private async Task<int> GetNumberOfPages(IDocument page)
    {
        var numberOfPagesInString = page.QuerySelector("div.message");
        var textNumberOfPages = numberOfPagesInString!.Text();
        int numberOfItems = int.Parse(string.Concat(textNumberOfPages!.Where(Char.IsDigit)));
        int numberOfPages = (int)Math.Ceiling((double)numberOfItems / DefaultPageSize);
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
        {
            // Start a new task and add it to the list
            downloadTasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(); // Wait for the semaphore to be available
                try
                {
                    return await DownloadImageAsync(page, appImage, cookies);
                }
                finally
                {
                    semaphore.Release(); // Release the semaphore once the task is done
                }
            }));
        }

        var results = await Task.WhenAll(downloadTasks);

        return results.ToList();
    }


    private async Task<AppImage> DownloadImageAsync(IBrowsingContext page, AppImage appImage, string cookie)
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
                cookieContainer.Add(new System.Net.Cookie(name: name, value: value, path: "/",
                    domain: "lessonsinlovegame.com"));
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

    private async Task<List<AppImage>> ExtractImagesAndTagsAsync(IBrowsingContext page, int numberOfPages)
    {
        List<AppImage> appImages = new List<AppImage>();
        for (var i = 1; i <= numberOfPages; i++)
        {
            Console.WriteLine($"Parsing page {i} of {numberOfPages}");
            var openAsync = await page.OpenAsync($"{RequestsUrl}?page={i}");
            var elements = openAsync.QuerySelectorAll(".requests > .block:not(.hidden) > .block-inner");


            foreach (var element in elements)
            {
                var imageUrl = element.QuerySelector("a");
                var imageSrc = imageUrl!.GetAttribute("href");
                var tagsElement = element.QuerySelector(".overlay > p.tags");
               
                var mediaIdElement = element.QuerySelector("div.like");
                var mediaIdAttribute = mediaIdElement.GetAttribute("data-media-id");
                var mediaId = int.Parse(mediaIdAttribute);
                var dateElement = element.QuerySelector(".overlay>p:nth-child(1)");
                var dateText = dateElement.Text();
                var date = await GetDateFromString(dateText);
                var tagsText = tagsElement != null ? tagsElement.TextContent : string.Empty;
                tagsText = tagsText.ToLower().Replace("tags:", "");
                if (tagsText.Trim() == "none yet")
                {
                    continue;
                }

                var fullImageUrl = SiteUrl + imageSrc;
                if (Path.GetExtension(new Uri(fullImageUrl).LocalPath)
                    .Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tags = tagsText.ToLower().Split(",").Select(x => new ImageTag()
                {
                    CreatedById = 1,
                    IsDeleted = false,
                    Name = x.Trim(),
                    CreatDateTime = DateTime.Now.ToUniversalTime(),
                }).ToList();
                var appImage = new AppImage()
                {
                    MediaId = mediaId,
                    Tags = tags,
                    IsDeleted = false,
                    IsHidden = false,
                    PathToFileOnDisc = imageSrc,
                    UploadedDate = date.ToUniversalTime()
                };
                appImages.Add(appImage);
            }
        }

        return appImages;
    }

    private static async Task<DateTime> GetDateFromString(string dateString)
    {
        string clearedInputString = dateString.Replace("Added: ", "").Trim();
        string format = "M/d/yyyy";
        return DateTime.ParseExact(clearedInputString, format, CultureInfo.InvariantCulture);
    }
    

    private async Task LoginAsync(IBrowsingContext page)
    {
        var openAsync = await page.OpenAsync(LoginUrl);
        var htmlFormElement = openAsync.Forms.FirstOrDefault();
        var dictionary = new Dictionary<string, string>()
        {
            { "loginModel.Username", _configuration["ParserLogin"] },
            { "loginModel.Password", _configuration["ParserPassword"] },
        };
        var formElement = htmlFormElement.SetValues(dictionary);
        var submitAsync = await formElement.SubmitAsync();
    }
    

    private static async Task SaveCookiesAsync(IBrowsingContext page)
    {
        var cookies = page.GetCookie(new Url(SiteUrl));
        var json = JsonSerializer.Serialize(cookies);
        await File.WriteAllTextAsync(CookiesFilePath, json);
        Console.WriteLine("Cookies saved.");
    }

    private static async Task LoadCookiesAsync(IBrowsingContext page)
    {
        var cookiesJson = await File.ReadAllTextAsync(CookiesFilePath);
        var cookies = JsonSerializer.Deserialize<string>(cookiesJson);
        var strings = cookies.Split(";", StringSplitOptions.RemoveEmptyEntries);
        foreach (var se in strings)
        {
            page.SetCookie(new Url(SiteUrl), se);
        }

        Console.WriteLine($"Cookies loaded. {page.GetCookie(new Url(SiteUrl))}");
    }
}