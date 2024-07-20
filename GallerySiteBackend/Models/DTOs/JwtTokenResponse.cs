namespace GallerySiteBackend.Models.DTOs;

public class JwtTokenResponse
{
    public string Token { get; set; }
    public string RefreshToken { get; set; }
}