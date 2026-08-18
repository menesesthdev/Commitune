namespace Commitune.Infrastructure.GitHub;

/// <summary>
/// A failure talking to GitHub's OAuth endpoints. Carries only GitHub's own error code —
/// never the response body, which is where the access token lives.
/// </summary>
public sealed class GitHubOAuthException(string error, string? description = null)
    : Exception(BuildMessage(error, description))
{
    public string Error { get; } = error;

    private static string BuildMessage(string error, string? description)
        => description is null ? $"GitHub OAuth failed: {error}." : $"GitHub OAuth failed: {error} ({description}).";
}
