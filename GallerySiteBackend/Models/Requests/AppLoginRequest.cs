namespace GallerySiteBackend.Models.Requests;

public class AppLoginRequest
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}