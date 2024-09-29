namespace Service.Contracts;

public interface IImageParserService
{
    public Task CheckUpdates();
    public Task DownloadAllImages();
}