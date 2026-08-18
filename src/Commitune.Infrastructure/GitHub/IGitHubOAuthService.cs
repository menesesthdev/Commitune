namespace Commitune.Infrastructure.GitHub;

public interface IGitHubOAuthService
{
    /// <summary>
    /// Builds the GitHub consent URL. <paramref name="state"/> must come from
    /// <see cref="Security.IOAuthStateProtector"/> — never a raw Telegram user id.
    /// </summary>
    Uri BuildAuthorizationUrl(string state);

    /// <summary>Exchanges the callback code for an access token.</summary>
    Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Revokes the token on GitHub's side, used by <c>/desconectar</c>.</summary>
    Task RevokeAsync(string accessToken, CancellationToken cancellationToken);
}

/// <summary>
/// Result of a successful code exchange. Holds a plaintext token: keep it in the request
/// scope, protect it before persisting, and never write it to a log.
/// </summary>
public sealed record GitHubAuthorization(string AccessToken, string Login);
