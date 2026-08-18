using System.Net;
using System.Text.Json;
using Commitune.Infrastructure.Configuration;
using Commitune.Infrastructure.GitHub;
using Microsoft.Extensions.Options;

namespace Commitune.Tests.GitHub;

public class GitHubOAuthServiceTests
{
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private readonly FakeHttpMessageHandler _handler = new();

    private static GitHubOptions Options() => new()
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        CallbackUrl = "https://commitune.test/oauth/github/callback",
    };

    private GitHubOAuthService CreateService()
    {
        var httpClient = new HttpClient(_handler);
        httpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new("Commitune", "1.0"));

        return new GitHubOAuthService(httpClient, Microsoft.Extensions.Options.Options.Create(Options()));
    }

    private void RespondWithToken()
        => _handler.Respond(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","token_type":"bearer","scope":"repo"}""");

    private void RespondWithUser(string login = "tester")
        => _handler.Respond(HttpStatusCode.OK, $$"""{"login":"{{login}}","id":1}""");

    [Fact]
    public void The_authorization_url_asks_for_the_scope_that_allows_private_repos()
    {
        var url = CreateService().BuildAuthorizationUrl("signed-state").ToString();

        // public_repo would make private repositories impossible — see CLAUDE.md.
        Assert.Contains("scope=repo", url, StringComparison.Ordinal);
        Assert.DoesNotContain("public_repo", url, StringComparison.Ordinal);
        Assert.Contains("state=signed-state", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exchanges_the_code_for_a_token_and_resolves_the_login()
    {
        RespondWithToken();
        RespondWithUser("menesesthdev");

        var authorization = await CreateService().ExchangeCodeAsync("the-code", CancellationToken.None);

        Assert.Equal(AccessToken, authorization.AccessToken);
        Assert.Equal("menesesthdev", authorization.Login);
    }

    /// <summary>
    /// Form-encoded is the shape GitHub documents for the token endpoint. Asserted because a
    /// wrong content type here breaks every single sign-up, and only in production.
    /// </summary>
    [Fact]
    public async Task Sends_the_client_credentials_and_the_code_form_encoded()
    {
        RespondWithToken();
        RespondWithUser();

        await CreateService().ExchangeCodeAsync("the-code", CancellationToken.None);

        var exchange = _handler.Requests[0];
        Assert.Equal("https://github.com/login/oauth/access_token", exchange.Uri.ToString());

        var fields = System.Web.HttpUtility.ParseQueryString(exchange.Body!);
        Assert.Equal("client-id", fields["client_id"]);
        Assert.Equal("client-secret", fields["client_secret"]);
        Assert.Equal("the-code", fields["code"]);
        Assert.Equal(Options().CallbackUrl, fields["redirect_uri"]);
    }

    [Fact]
    public async Task Presents_the_new_token_when_looking_the_user_up()
    {
        RespondWithToken();
        RespondWithUser();

        await CreateService().ExchangeCodeAsync("the-code", CancellationToken.None);

        var lookup = _handler.Requests[1];
        Assert.Equal("https://api.github.com/user", lookup.Uri.ToString());
        Assert.Equal($"Bearer {AccessToken}", lookup.Authorization);
    }

    /// <summary>GitHub reports OAuth failures as 200 with an error body, not as a 4xx.</summary>
    [Fact]
    public async Task Treats_an_error_body_on_a_200_as_a_failure()
    {
        _handler.Respond(
            HttpStatusCode.OK,
            """{"error":"bad_verification_code","error_description":"The code passed is incorrect or expired."}""");

        var exception = await Assert.ThrowsAsync<GitHubOAuthException>(
            () => CreateService().ExchangeCodeAsync("stale-code", CancellationToken.None));

        Assert.Equal("bad_verification_code", exception.Error);
    }

    [Fact]
    public async Task Fails_when_github_answers_with_an_http_error()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable, "<html>down</html>", "text/html");

        await Assert.ThrowsAsync<GitHubOAuthException>(
            () => CreateService().ExchangeCodeAsync("the-code", CancellationToken.None));
    }

    [Fact]
    public async Task Fails_when_the_login_cannot_be_resolved()
    {
        RespondWithToken();
        _handler.Respond(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        await Assert.ThrowsAsync<GitHubOAuthException>(
            () => CreateService().ExchangeCodeAsync("the-code", CancellationToken.None));
    }

    /// <summary>
    /// CLAUDE.md: never log a token, "not inside an exception message". The exchange response
    /// body is the one place a token appears, so no failure may echo it.
    /// </summary>
    [Fact]
    public async Task Never_puts_the_token_in_an_exception()
    {
        // A body that is both an error and carries a token — the worst case for leaking.
        _handler.Respond(
            HttpStatusCode.BadRequest,
            $$"""{"access_token":"{{AccessToken}}","error":"invalid_client"}""");

        var exception = await Assert.ThrowsAsync<GitHubOAuthException>(
            () => CreateService().ExchangeCodeAsync("the-code", CancellationToken.None));

        Assert.DoesNotContain(AccessToken, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("gho_", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoking_sends_the_token_to_the_grant_endpoint_with_basic_auth()
    {
        _handler.Respond(HttpStatusCode.NoContent, string.Empty);

        await CreateService().RevokeAsync(AccessToken, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://api.github.com/applications/client-id/grant", request.Uri.ToString());
        Assert.StartsWith("Basic ", request.Authorization!, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(AccessToken, body.RootElement.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task Revoking_an_authorization_github_no_longer_has_is_not_an_error()
    {
        // 404 means the grant is already gone — the end state the caller wanted.
        _handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        await CreateService().RevokeAsync(AccessToken, CancellationToken.None);
    }
}
