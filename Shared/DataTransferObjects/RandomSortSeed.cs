using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Shared.DataTransferObjects;

public static class RandomSortSeed
{
    private const int SeedByteLength = sizeof(long);
    private const int SeedTextLength = 11;

    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[SeedByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Encode(bytes);
    }

    public static bool TryGetValue(string? value, out long seed)
    {
        seed = default;
        if (value is null || value.Length != SeedTextLength || value.Any(character =>
                !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != SeedByteLength || !string.Equals(value, Encode(bytes), StringComparison.Ordinal))
                return false;

            seed = BinaryPrimitives.ReadInt64BigEndian(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
