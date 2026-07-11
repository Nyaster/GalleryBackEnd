using System.ComponentModel.DataAnnotations;

namespace Service;

public sealed class ParserSettings
{
    public bool Enabled { get; init; }
    [Required] public string SiteUrl { get; init; } = "https://lessonsinlovegame.com";
    [Required] public string LoginUrl { get; init; } = "https://lessonsinlovegame.com/account/login/";
    [Required] public string RequestsUrl { get; init; } = "https://lessonsinlovegame.com/galleries/requests";
    [Required] public string ParserLogin { get; init; } = string.Empty;
    [Required] public string ParserPassword { get; init; } = string.Empty;
    [Range(1, 300)] public int LoginTimeoutSeconds { get; init; } = 30;
    [Range(1, 10)] public int DownloadConcurrency { get; init; } = 3;
    [Range(1, 100)] public int IncrementalPages { get; init; } = 5;
}
