using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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

    private const string AccessTokenEndpoint = "https://github.com/login/oauth/access_token";

    private const string UserEndpoint = "https://api.github.com/user";

    private const string GrantEndpoint = "https://api.github.com/applications/{0}/grant";

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

    public async Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var accessToken = await RequestAccessTokenAsync(code, cancellationToken);
        var login = await ResolveLoginAsync(accessToken, cancellationToken);

        return new GitHubAuthorization(accessToken, login);
    }

    public async Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        // DELETE .../grant drops the whole authorization, not just this token, which is what
        // /desconectar promises the user.
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            string.Format(null, GrantEndpoint, _options.ClientId))
        {
            Content = JsonContent.Create(new RevokeRequest(accessToken)),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BasicCredentials());

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // 404 means GitHub has no such grant — the end state the caller wanted either way.
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new GitHubOAuthException("revoke_failed", $"GitHub answered {(int)response.StatusCode}.");
        }
    }

    private async Task<string> RequestAccessTokenAsync(string code, CancellationToken cancellationToken)
    {
        // Form-encoded, which is the shape GitHub documents for this endpoint. Accept:
        // application/json (set on the client) is what makes the *answer* come back as JSON.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = _options.CallbackUrl,
        });

        using var response = await _httpClient.PostAsync(AccessTokenEndpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body is not read on purpose: on this endpoint it can carry a token.
            throw new GitHubOAuthException("token_request_failed", $"GitHub answered {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken)
            ?? throw new GitHubOAuthException("empty_token_response");

        // GitHub reports OAuth failures as 200 with an error body, not as a 4xx.
        if (!string.IsNullOrEmpty(payload.Error))
        {
            throw new GitHubOAuthException(payload.Error, payload.ErrorDescription);
        }

        if (string.IsNullOrEmpty(payload.AccessToken))
        {
            throw new GitHubOAuthException("missing_access_token");
        }

        return payload.AccessToken;
    }

    private async Task<string> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubOAuthException("user_lookup_failed", $"GitHub answered {(int)response.StatusCode}.");
        }

        var user = await response.Content.ReadFromJsonAsync<UserResponse>(cancellationToken);

        return string.IsNullOrEmpty(user?.Login)
            ? throw new GitHubOAuthException("missing_login")
            : user.Login;
    }

    private string BasicCredentials()
        => Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record UserResponse([property: JsonPropertyName("login")] string? Login);

    private sealed record RevokeRequest([property: JsonPropertyName("access_token")] string AccessToken);
}
