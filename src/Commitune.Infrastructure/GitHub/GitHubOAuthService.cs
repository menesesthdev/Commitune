using Commitune.Infrastructure.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Commitune.Infrastructure.GitHub;

public sealed class GitHubOAuthService : IGitHubOAuthService
{
    /// <summary>
    /// <c>repo</c> is the narrowest scope that still allows creating a *private* repository
    /// and committing to it. Anything narrower (e.g. <c>public_repo</c>) would force public repos.
    /// </summary>
    public const string Scopes = "repo";

    private const string AuthorizeEndpoint = "https://github.com/login/oauth/authorize";

    private readonly HttpClient _httpClient;
    private readonly GitHubOptions _options;

    public GitHubOAuthService(HttpClient httpClient, IOptions<GitHubOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Uri BuildAuthorizationUrl(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.CallbackUrl,
            ["scope"] = Scopes,
            ["state"] = state,
        };

        return new Uri(QueryHelpers.AddQueryString(AuthorizeEndpoint, query));
    }

    public Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        // TODO: POST https://github.com/login/oauth/access_token (Accept: application/json)
        // with client_id, client_secret, code and redirect_uri, then GET /user with the
        // resulting token to resolve the login. Never log the response body — it carries the token.
        throw new NotImplementedException("GitHub code exchange is not wired up yet.");
    }

    public Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        // TODO: DELETE /applications/{client_id}/grant with Basic auth (client_id:client_secret)
        // and the token in the body, so /desconectar really revokes access on GitHub's side.
        throw new NotImplementedException("GitHub token revocation is not wired up yet.");
    }
}
