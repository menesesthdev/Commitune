using System.Net;
using Commitune.Api.Endpoints;
using Commitune.Domain.Onboarding;

namespace Commitune.Tests.Api;

/// <summary>
/// The other half of onboarding, over real HTTP: GitHub redirects a browser here, and what
/// arrives is a query string an attacker can also type. The state is the whole defence.
/// </summary>
public class GitHubCallbackEndpointTests : IDisposable
{
    private const long TelegramUserId = 4242;

    private readonly CommituneAppFactory _app = new();

    public void Dispose()
    {
        _app.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The state the user is in when GitHub redirects them here: /start already ran.</summary>
    private Task SeedUserAwaitingAuthAsync()
        => _app.SeedUserAsync(TelegramUserId, OnboardingState.AwaitingGithubAuth);

    private Task<HttpResponseMessage> CallbackAsync(string query)
        => _app.CreateClient().GetAsync($"{GitHubOAuthEndpoints.CallbackRoute}?{query}");

    [Fact]
    public async Task A_signed_state_completes_the_connection()
    {
        await SeedUserAwaitingAuthAsync();
        var state = _app.CreateOAuthState(TelegramUserId);

        var response = await CallbackAsync($"code=abc123&state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GitHub conectado", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("abc123", _app.GitHubOAuth.ExchangedCode);

        var user = await _app.FindUserAsync(TelegramUserId);
        Assert.Equal(OnboardingState.AwaitingRepoName, user!.State);
    }

    /// <summary>
    /// The token is written encrypted or not at all — the column holds ciphertext by contract.
    /// </summary>
    [Fact]
    public async Task The_token_never_reaches_the_database_in_plaintext()
    {
        await SeedUserAwaitingAuthAsync();
        var state = _app.CreateOAuthState(TelegramUserId);

        await CallbackAsync($"code=abc123&state={Uri.EscapeDataString(state)}");

        var user = await _app.FindUserAsync(TelegramUserId);

        Assert.NotNull(user!.ProtectedGithubToken);
        Assert.DoesNotContain(
            _app.GitHubOAuth.Authorization.AccessToken,
            user.ProtectedGithubToken,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_user_is_pulled_back_into_the_chat_to_name_the_repository()
    {
        await SeedUserAwaitingAuthAsync();
        var state = _app.CreateOAuthState(TelegramUserId);

        await CallbackAsync($"code=abc123&state={Uri.EscapeDataString(state)}");

        var sent = Assert.Single(_app.Messenger.Sent);
        Assert.Equal(TelegramUserId, sent.ChatId);
        Assert.Contains("repositório", sent.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The state carries the telegram_user_id. Accepting an unsigned one would let anyone
    /// attach their own GitHub account to someone else's chat — the account-takeover vector
    /// CLAUDE.md names.
    /// </summary>
    [Theory]
    [InlineData("4242")]
    [InlineData("")]
    [InlineData("nao-assinado")]
    public async Task An_unsigned_state_is_refused(string state)
    {
        await SeedUserAwaitingAuthAsync();

        var response = await CallbackAsync($"code=abc123&state={Uri.EscapeDataString(state)}");

        Assert.Contains("Link inválido", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var user = await _app.FindUserAsync(TelegramUserId);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user!.State);
        Assert.Null(user.ProtectedGithubToken);
        Assert.Null(_app.GitHubOAuth.ExchangedCode);
    }

    [Fact]
    public async Task A_state_signed_for_another_application_is_refused()
    {
        await SeedUserAwaitingAuthAsync();

        // Same shape, different key ring: what a forged state looks like from the outside.
        var foreign = new Infrastructure.Security.DataProtectionOAuthStateProtector(
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("outro-app"),
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Configuration.GitHubOptions()))
            .Create(TelegramUserId);

        var response = await CallbackAsync($"code=abc123&state={Uri.EscapeDataString(foreign)}");

        Assert.Contains("Link inválido", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(_app.GitHubOAuth.ExchangedCode);
    }

    [Fact]
    public async Task Declining_on_githubs_consent_screen_lands_somewhere_that_explains_it()
    {
        var response = await CallbackAsync("error=access_denied&error_description=The+user+has+denied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("cancelou", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(_app.GitHubOAuth.ExchangedCode);
    }
}
