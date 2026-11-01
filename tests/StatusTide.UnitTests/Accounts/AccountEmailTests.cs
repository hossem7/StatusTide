using StatusTide.Api.Features.Accounts;

namespace StatusTide.UnitTests.Accounts;

public class AccountEmailTests
{
    [Theory]
    [InlineData("person@acme.example", "person@acme.example")]
    [InlineData("  Person@ACME.example ", "person@acme.example")]
    public void A_plain_address_is_trimmed_and_lower_cased(string input, string expected)
    {
        Assert.Equal(expected, AccountEmail.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an email")]
    [InlineData("person@localhost")]
    [InlineData("Name <person@acme.example>")]
    [InlineData("person@acme.example\r\nBcc: other@acme.example")]
    public void Anything_else_is_refused(string? input)
    {
        Assert.Null(AccountEmail.Normalize(input));
    }

    [Fact]
    public void An_overlong_address_is_refused()
    {
        Assert.Null(AccountEmail.Normalize(new string('a', AccountEmail.MaxLength) + "@acme.example"));
    }
}
