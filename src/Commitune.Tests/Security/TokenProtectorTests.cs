using System.Security.Cryptography;
using Commitune.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Commitune.Tests.Security;

public class TokenProtectorTests
{
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private static DataProtectionTokenProtector CreateProtector(string keyRing = "commitune-tests")
        => new(DataProtectionProvider.Create(keyRing));

    [Fact]
    public void Roundtrips_the_access_token()
    {
        var protector = CreateProtector();

        var protectedToken = protector.Protect(AccessToken);

        Assert.Equal(AccessToken, protector.Unprotect(protectedToken));
    }

    [Fact]
    public void Never_stores_the_token_in_the_clear()
    {
        var protectedToken = CreateProtector().Protect(AccessToken);

        Assert.DoesNotContain(AccessToken, protectedToken, StringComparison.Ordinal);
        Assert.DoesNotContain("gho_", protectedToken, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_payload_from_a_different_key_ring()
    {
        var protectedToken = CreateProtector("somewhere-else").Protect(AccessToken);

        Assert.Throws<CryptographicException>(() => CreateProtector().Unprotect(protectedToken));
    }
}
