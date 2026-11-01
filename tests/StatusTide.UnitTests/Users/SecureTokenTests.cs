using System.Security.Cryptography;

using StatusTide.Data.Users;

namespace StatusTide.UnitTests.Users;

public class SecureTokenTests
{
    [Fact]
    public void A_new_token_hashes_to_its_stored_hash()
    {
        var (token, hash) = SecureToken.Create();

        Assert.Equal(hash, SecureToken.Hash(token));
        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void Two_tokens_are_never_the_same()
    {
        Assert.NotEqual(SecureToken.Create().Token, SecureToken.Create().Token);
    }

    [Fact]
    public void The_token_is_safe_in_a_cookie()
    {
        var (token, _) = SecureToken.Create();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!")]
    [InlineData("AAAA")] // valid, but only 3 bytes
    public void Anything_else_has_no_hash(string? cookie)
    {
        Assert.Null(SecureToken.Hash(cookie));
    }

    [Fact]
    public void An_overlong_cookie_is_rejected_before_decoding()
    {
        Assert.Null(SecureToken.Hash(new string('A', 10_000)));
    }

    [Fact]
    public void The_hash_is_plain_sha256_of_the_token_bytes()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(SHA256.HashData(bytes), SecureToken.Hash(token));
    }
}
