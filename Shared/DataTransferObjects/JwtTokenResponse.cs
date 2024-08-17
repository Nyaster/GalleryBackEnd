namespace Shared.DataTransferObjects;

public class JwtTokenResponse
{
    public string Token { get; set; }
    public string RefreshToken { get; set; }
}