using System.ComponentModel.DataAnnotations;

namespace GallerySiteBackend.Configuration;

public sealed class JwtConfiguration
{
    [Required] public string ValidIssuer { get; init; } = string.Empty;
    [Required] public string ValidAudience { get; init; } = string.Empty;
    [Required, MinLength(32)] public string SecretKey { get; init; } = string.Empty;
    [Range(5, 60)] public int AccessTokenMinutes { get; init; } = 15;
    [Range(1, 30)] public int RefreshTokenDays { get; init; } = 7;
    [Required] public string RefreshCookieName { get; init; } = "gallery_refresh";
    public bool RefreshCookieSecure { get; init; } = true;
}
