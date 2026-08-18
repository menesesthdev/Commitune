using Commitune.Infrastructure.Configuration;
using Commitune.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Commitune.Tests.Security;

public class OAuthStateProtectorTests
{
    private const long TelegramUserId = 8675309;

    private static DataProtectionOAuthStateProtector CreateProtector(string keyRing = "commitune-tests")
        => new(DataProtectionProvider.Create(keyRing), Options.Create(new GitHubOptions()));

    [Fact]
    public void Roundtrips_the_telegram_user_id()
    {
        var protector = CreateProtector();

        var state = protector.Create(TelegramUserId);

        Assert.True(protector.TryValidate(state, out var recovered));
        Assert.Equal(TelegramUserId, recovered);
    }

    [Fact]
    public void Does_not_expose_the_user_id_in_the_state()
    {
        var state = CreateProtector().Create(TelegramUserId);

        Assert.DoesNotContain(TelegramUserId.ToString(), state, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_tampered_state()
    {
        var protector = CreateProtector();
        var state = protector.Create(TelegramUserId);

        // Flip one character of the payload.
        var tampered = state[..^1] + (state[^1] == 'A' ? 'B' : 'A');

        Assert.False(protector.TryValidate(tampered, out _));
    }

    [Fact]
    public void Rejects_a_state_signed_with_a_different_key_ring()
    {
        var state = CreateProtector("attacker").Create(TelegramUserId);

        Assert.False(CreateProtector().TryValidate(state, out _));
    }

    [Fact]
    public void Rejects_an_unsigned_user_id()
    {
        Assert.False(CreateProtector().TryValidate(TelegramUserId.ToString(), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_state(string? state)
    {
        Assert.False(CreateProtector().TryValidate(state, out _));
    }

    [Fact]
    public void Rejects_an_expired_state()
    {
        var options = Options.Create(new GitHubOptions { StateLifetime = TimeSpan.FromMilliseconds(-1) });
        var protector = new DataProtectionOAuthStateProtector(
            DataProtectionProvider.Create("commitune-tests"),
            options);

        Assert.False(protector.TryValidate(protector.Create(TelegramUserId), out _));
    }
}
