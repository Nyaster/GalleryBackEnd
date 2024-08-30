using Entities.Models;

namespace GallerySiteBackend.Models;

public class AppUser
{
    public int Id { get; set; }
    public  string Login { get; set; }
    public  string Password { get; set; }
    public string Salt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExp { get; set; }
    public bool IsRefreshTokenExpired => RefreshTokenExp < DateTime.Now;
    public List<AppUserRoles> AppUserRolesList { get; set; } = new List<AppUserRoles>();
    public List<AppImage> UploadedImages { get; set; }
}

public enum AppUserRoles
{
    Admin,
    Moderator,
    User
}