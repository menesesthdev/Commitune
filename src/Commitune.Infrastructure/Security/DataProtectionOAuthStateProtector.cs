using System.Globalization;
using System.Security.Cryptography;
using Commitune.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Commitune.Infrastructure.Security;

/// <summary>
/// Data Protection payloads are encrypt-then-MAC, so the state is both confidential and
/// tamper-evident; the time-limited protector adds the expiry check on top.
/// </summary>
public sealed class DataProtectionOAuthStateProtector : IOAuthStateProtector
{
    public const string Purpose = "Commitune.GithubOAuthState.v1";

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    public DataProtectionOAuthStateProtector(
        IDataProtectionProvider provider,
        IOptions<GitHubOptions> options)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _lifetime = options.Value.StateLifetime;
    }

    public string Create(long telegramUserId)
        => _protector.Protect(telegramUserId.ToString(CultureInfo.InvariantCulture), _lifetime);

    public bool TryValidate(string? state, out long telegramUserId)
    {
        telegramUserId = 0;

        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(state);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or produced by a different key ring.
            return false;
        }

        return long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out telegramUserId);
    }
}
