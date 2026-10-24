using System.Security.Cryptography;
using System.Text.Json;
using Entities.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Service.Contracts;

namespace Service;

public sealed class ForMeFeedCursorProtector(IDataProtectionProvider provider) : IForMeFeedCursorProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Lilgallery.ForMeFeed.Cursor.v1");

    public string Protect(ForMeFeedCursor cursor) => _protector.Protect(JsonSerializer.Serialize(cursor));

    public ForMeFeedCursor Unprotect(string token)
    {
        try
        {
            if (token.Length > 4096) throw new JsonException();
            var cursor = JsonSerializer.Deserialize<ForMeFeedCursor>(_protector.Unprotect(token));
            if (cursor is null || cursor.Version != 1 || cursor.FeedId == Guid.Empty || cursor.UserId < 1 ||
                cursor.LastRank is < 0 or > 500 || cursor.PageSize is < 1 or > 50 ||
                cursor.ExpiresAtUtc == default)
                throw new JsonException();
            return cursor;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            throw new Base400BadRequestException("Invalid recommendation cursor.");
        }
    }
}
