using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Entities.Exceptions;
using Entities.Models;
using Shared.DataTransferObjects;

namespace Service;

internal static class AuthenticatorBackupCodes
{
    public static AuthenticatorBackupCodesResponse Replace(AppUser user)
    {
        var codes = new List<string>(10);
        var hashes = new List<string>(10);
        for (var index = 0; index < 10; index++)
        {
            // 128 bits of entropy per code; a fast hash is safe for these random secrets.
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            codes.Add(string.Join("-", Enumerable.Range(0, 4).Select(part => raw.Substring(part * 8, 8))));
            hashes.Add(Convert.ToHexString(Hash(user.Id, raw)));
        }

        user.AuthenticatorBackupCodeHashes = hashes;
        return new AuthenticatorBackupCodesResponse(codes);
    }

    // Caller must hold the user row lock and commit consumption with the authorized action.
    public static bool TryConsume(AppUser user, string? code, bool preserveLastCode = false)
    {
        if (user.AuthenticatorEnabledAtUtc is null || code is null || code.Length > 64)
            return false;
        var normalized = code.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 32 || normalized.Any(character => !char.IsAsciiHexDigit(character)))
            return false;
        var hash = Hash(user.Id, normalized);
        var matchedIndex = -1;
        for (var index = 0; index < user.AuthenticatorBackupCodeHashes.Count; index++)
        {
            if (CryptographicOperations.FixedTimeEquals(hash,
                    Convert.FromHexString(user.AuthenticatorBackupCodeHashes[index])))
                matchedIndex = index;
        }

        if (matchedIndex < 0)
            return false;
        if (preserveLastCode && user.AuthenticatorBackupCodeHashes.Count == 1)
            throw new Base409ConflictException(
                "This is your last backup code. Use it to remove the lost authenticator, then start a new setup.");
        user.AuthenticatorBackupCodeHashes.RemoveAt(matchedIndex);
        return true;
    }

    private static byte[] Hash(int userId, string code)
        => SHA256.HashData(Encoding.UTF8.GetBytes("Lilgallery.BackupCode.v1:" +
                                                  userId.ToString(CultureInfo.InvariantCulture) + ":" + code));
}