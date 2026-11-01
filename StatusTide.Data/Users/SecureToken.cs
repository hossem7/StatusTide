using System.Buffers.Text;
using System.Security.Cryptography;

namespace StatusTide.Data.Users;

/// <summary>
/// A secret we hand out and later check, such as a guest cookie, a refresh token or an email confirmation link: 32 random bytes. Only its SHA-256 hash is stored, so the
/// lookup needs no salt (the token is already random) and a leaked database signs nobody in.
/// </summary>
public static class SecureToken
{
    public static (string Token, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>The hash of a presented token, or null when it isn't a token we could have made.</summary>
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
