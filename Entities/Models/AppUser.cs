namespace Entities.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string NormalizedLogin { get; set; }
    public required string PasswordHash { get; set; }
    public List<AppUserRole> Roles { get; set; } = [AppUserRole.User];
    public List<AppImage> UploadedImages { get; set; } = [];
    public List<RefreshSession> RefreshSessions { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum AppUserRole
{
    Admin,
    Moderator,
    User
}
