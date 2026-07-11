namespace Entities.Models;

public sealed class ScrapeRun
{
    public Guid Id { get; set; }
    public ScrapeMode Mode { get; set; }
    public BackgroundJobStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int ImagesDiscovered { get; set; }
    public int ImagesImported { get; set; }
    public string? Error { get; set; }
}

public enum ScrapeMode { Incremental, Full }
public enum BackgroundJobStatus { Queued, Running, Completed, Failed, Cancelled }
