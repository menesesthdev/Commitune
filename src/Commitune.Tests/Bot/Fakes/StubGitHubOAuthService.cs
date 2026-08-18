using Commitune.Infrastructure.GitHub;

namespace Commitune.Tests.Bot.Fakes;

/// <summary>
/// Only the authorization URL matters at this stage; the code exchange belongs to the next
/// slice and any test that reaches it is testing something it did not mean to.
/// </summary>
public sealed class StubGitHubOAuthService : IGitHubOAuthService
{
    public Uri BuildAuthorizationUrl(string state)
        => new($"https://github.com/login/oauth/authorize?client_id=test&state={Uri.EscapeDataString(state)}");

    public Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
        => throw new NotSupportedException("The conversation must not exchange OAuth codes.");

    public Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
        => throw new NotSupportedException("The conversation must not revoke tokens.");
}
