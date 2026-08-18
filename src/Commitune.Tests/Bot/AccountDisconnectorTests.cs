using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Security;
using Commitune.Tests.Bot.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Commitune.Tests.Bot;

public class AccountDisconnectorTests
{
    private const long TelegramUserId = 4242;
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private readonly FakeBotUserStore _users = new();
    private readonly RecordingGitHubOAuthService _gitHub = new();

    private readonly DataProtectionTokenProtector _tokenProtector =
        new(DataProtectionProvider.Create("commitune-tests"));

    private AccountDisconnector CreateDisconnector()
        => new(_users, _gitHub, _tokenProtector, NullLogger<AccountDisconnector>.Instance);

    private BotUser SeedConnectedUser()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.Ready);
        user.ProtectedGithubToken = _tokenProtector.Protect(AccessToken);
        user.GithubLogin = "tester";
        user.RepositoryOwner = "tester";
        user.RepositoryName = "til";

        return user;
    }

    [Fact]
    public async Task Revokes_the_grant_on_github()
    {
        var user = SeedConnectedUser();

        var outcome = await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Equal(DisconnectOutcome.Disconnected, outcome);
        Assert.Equal(AccessToken, _gitHub.RevokedToken);
    }

    /// <summary>
    /// "Wipe it from storage" means all of it: a repository name and a login left behind are
    /// still a record of the account the user just disconnected.
    /// </summary>
    [Fact]
    public async Task Leaves_nothing_about_the_github_account_behind()
    {
        var user = SeedConnectedUser();

        await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Null(user.ProtectedGithubToken);
        Assert.Null(user.GithubLogin);
        Assert.Null(user.RepositoryOwner);
        Assert.Null(user.RepositoryName);
        Assert.Equal(OnboardingState.NotStarted, user.State);
    }

    /// <summary>
    /// The user asked to be disconnected. Keeping their token here because a remote call failed
    /// would be the opposite of what they asked for — so the wipe happens either way, and the
    /// outcome is what tells them the revocation is unconfirmed.
    /// </summary>
    [Fact]
    public async Task Wipes_the_token_even_when_github_refuses_to_revoke()
    {
        var user = SeedConnectedUser();
        _gitHub.FailWith = new GitHubOAuthException("revoke_failed", "GitHub answered 500.");

        var outcome = await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Equal(DisconnectOutcome.DisconnectedWithoutRevoking, outcome);
        Assert.Null(user.ProtectedGithubToken);
        Assert.Equal(OnboardingState.NotStarted, user.State);
    }

    [Fact]
    public async Task Wipes_the_token_even_when_the_network_is_down()
    {
        var user = SeedConnectedUser();
        _gitHub.FailWith = new HttpRequestException("a rede caiu");

        var outcome = await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Equal(DisconnectOutcome.DisconnectedWithoutRevoking, outcome);
        Assert.Null(user.ProtectedGithubToken);
    }

    /// <summary>
    /// A lost key ring makes the stored token unreadable. There is nothing to revoke with, but
    /// the ciphertext still has to go.
    /// </summary>
    [Fact]
    public async Task Wipes_a_token_it_cannot_even_decrypt()
    {
        var user = SeedConnectedUser();
        user.ProtectedGithubToken = new DataProtectionTokenProtector(
            DataProtectionProvider.Create("a-lost-key-ring")).Protect(AccessToken);

        var outcome = await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Equal(DisconnectOutcome.DisconnectedWithoutRevoking, outcome);
        Assert.Null(user.ProtectedGithubToken);
        Assert.Null(_gitHub.RevokedToken);
    }

    [Fact]
    public async Task Says_so_when_there_was_nothing_connected()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.NotStarted);

        var outcome = await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Equal(DisconnectOutcome.NothingToDisconnect, outcome);
        Assert.Null(_gitHub.RevokedToken);
    }

    /// <summary>
    /// A repository reference that outlived its token still points at the user's GitHub. It
    /// goes with everything else.
    /// </summary>
    [Fact]
    public async Task Clears_a_repository_left_without_a_token()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth);
        user.RepositoryOwner = "tester";
        user.RepositoryName = "til";

        await CreateDisconnector().DisconnectAsync(user, CancellationToken.None);

        Assert.Null(user.RepositoryName);
        Assert.Equal(OnboardingState.NotStarted, user.State);
    }

    private sealed class RecordingGitHubOAuthService : IGitHubOAuthService
    {
        public string? RevokedToken { get; private set; }

        public Exception? FailWith { get; set; }

        public Uri BuildAuthorizationUrl(string state) => new("https://github.com/login/oauth/authorize");

        public Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException(FailWith);
            }

            RevokedToken = accessToken;

            return Task.CompletedTask;
        }
    }
}
