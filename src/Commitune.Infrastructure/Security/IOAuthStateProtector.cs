namespace Commitune.Infrastructure.Security;

/// <summary>
/// Issues and verifies the OAuth <c>state</c> parameter, which carries the Telegram user id
/// across the GitHub round trip. An unauthenticated state would let an attacker bind their
/// own GitHub account to somebody else's Telegram id, so the value is always authenticated
/// and always expires.
/// </summary>
public interface IOAuthStateProtector
{
    /// <summary>Creates an authenticated, URL-safe state for the given Telegram user.</summary>
    string Create(long telegramUserId);

    /// <summary>
    /// Verifies authenticity and expiry. Returns <c>false</c> for anything tampered with,
    /// expired, or not issued by this application.
    /// </summary>
    bool TryValidate(string? state, out long telegramUserId);
}
