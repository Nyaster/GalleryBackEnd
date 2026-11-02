namespace Shared.DataTransferObjects;

public sealed record JwtTokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, AppUserDto User);
