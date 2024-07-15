namespace GallerySiteBackend.Models;

public class AppUser
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string Password { get; set; }
    public string Salt { get; set; }
    public string RefreshToken { get; set; }
    public DateTime? RefreshTokenExp { get; set; }
    public bool IsRefreshTokenExpired => RefreshTokenExp < DateTime.Now;
    public List<AppUserRoles> AppUserRolesList { get; set; } = new List<AppUserRoles>();
}

public enum AppUserRoles
{
    Admin,
    Moderator,
    User
}