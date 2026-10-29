using System.Buffers.Text;
using System.Security.Cryptography;

namespace StatusTide.Api.Features.Guests;

/// <summary>
/// The secret in a guest's cookie: 32 random bytes. Only its SHA-256 hash is stored, so the
/// lookup needs no salt (the token is already random) and a leaked database signs nobody in.
/// </summary>
public static class GuestToken
{
    public static (string Token, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>The hash of a cookie value, or null when it isn't a token we could have made.</summary>
    public static byte[]? Hash(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[48];
        if (!Base64Url.IsValid(token) || !Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != 32)
        {
            return null;
        }

        return SHA256.HashData(bytes[..written]);
    }
}
