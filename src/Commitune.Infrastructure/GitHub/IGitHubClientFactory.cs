using Octokit;

namespace Commitune.Infrastructure.GitHub;

/// <summary>
/// Builds an Octokit client bound to one user's token, for the duration of one request.
/// Exists as a seam so the calls made against GitHub — above all "is this repository
/// private?" — can be asserted in tests instead of taken on trust.
/// </summary>
public interface IGitHubClientFactory
{
    IGitHubClient Create(string accessToken);
}

public sealed class GitHubClientFactory : IGitHubClientFactory
{
    /// <summary>User-Agent GitHub sees. Required by the API.</summary>
    public static readonly ProductHeaderValue Product = new("Commitune");

    public IGitHubClient Create(string accessToken)
        => new GitHubClient(Product) { Credentials = new Credentials(accessToken) };
}
