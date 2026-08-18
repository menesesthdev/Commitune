namespace Commitune.Infrastructure.Security;

/// <summary>
/// Encrypts GitHub access tokens for storage. The plaintext token exists only inside the
/// request scope that needs it — it is never persisted, logged or serialized anywhere else.
/// </summary>
public interface ITokenProtector
{
    string Protect(string accessToken);

    string Unprotect(string protectedAccessToken);
}
