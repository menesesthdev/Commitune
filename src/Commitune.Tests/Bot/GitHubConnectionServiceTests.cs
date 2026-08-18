using Commitune.Api.Bot;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Security;
using Commitune.Tests.Bot.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Commitune.Tests.Bot;

public class GitHubConnectionServiceTests
{
    private const long TelegramUserId = 4242;
    private const long ChatId = 909090;
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private readonly FakeBotUserStore _users = new();
    private readonly RecordingBotMessenger _messenger = new();
    private readonly FakeGitHubOAuthService _oauth = new() { Authorization = new(AccessToken, "menesesthdev") };

    private readonly DataProtectionTokenProtector _tokenProtector =
        new(DataProtectionProvider.Create("commitune-tests"));

    private GitHubConnectionService CreateService()
        => new(_users, _oauth, _tokenProtector, _messenger, NullLogger<GitHubConnectionService>.Instance);

    [Fact]
    public async Task Moves_an_authorized_user_to_the_repo_name_step()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);

        var outcome = await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.Equal(GitHubConnectionOutcome.AwaitingRepoName, outcome);
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
        Assert.Equal("menesesthdev", user.GithubLogin);
    }

    [Fact]
    public async Task Asks_for_the_repository_name_in_the_telegram_chat()
    {
        _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);

        await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        var sent = _messenger.Single;
        Assert.Equal(ChatId, sent.ChatId);
        Assert.Equal(BotReplies.AskRepoName, sent.Text);
    }

    /// <summary>CLAUDE.md: tokens are stored encrypted, and there is no plaintext path.</summary>
    [Fact]
    public async Task Stores_the_token_encrypted_and_never_in_the_clear()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);

        await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.NotNull(user.ProtectedGithubToken);
        Assert.DoesNotContain(AccessToken, user.ProtectedGithubToken, StringComparison.Ordinal);
        Assert.Equal(AccessToken, _tokenProtector.Unprotect(user.ProtectedGithubToken));
    }

    [Fact]
    public async Task Never_leaks_the_token_into_a_message_to_the_user()
    {
        _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);

        await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.All(_messenger.Sent, sent =>
            Assert.DoesNotContain(AccessToken, sent.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refreshes_the_token_of_a_user_who_already_finished_onboarding()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.Ready, ChatId);
        user.RepositoryOwner = "tester";
        user.RepositoryName = "diario";

        var outcome = await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.Equal(GitHubConnectionOutcome.Reconnected, outcome);

        // Reauthorizing must not drag a working user back through onboarding.
        Assert.Equal(OnboardingState.Ready, user.State);
        Assert.Equal("diario", user.RepositoryName);
        Assert.Equal(BotReplies.Reconnected, _messenger.Single.Text);
    }

    [Fact]
    public async Task Reports_an_unknown_user_without_touching_github()
    {
        var outcome = await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.Equal(GitHubConnectionOutcome.UnknownUser, outcome);
        Assert.Null(_oauth.ExchangedCode);
        Assert.Empty(_messenger.Sent);
    }

    [Fact]
    public async Task Tells_the_user_when_github_refuses_the_code()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);
        _oauth.FailWith = new GitHubOAuthException("bad_verification_code");

        var outcome = await CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None);

        Assert.Equal(GitHubConnectionOutcome.Failed, outcome);
        Assert.Equal(BotReplies.AuthorizationFailed, _messenger.Single.Text);

        // A failed exchange must leave nothing behind.
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(user.ProtectedGithubToken);
    }

    [Fact]
    public async Task Keeps_the_authorization_even_if_telegram_cannot_be_reached()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingGithubAuth, ChatId);
        _messenger.FailWith = new HttpRequestException("telegram unreachable");

        // The send throws out of CompleteAsync — the endpoint still renders a page, and the
        // user can recover with /start because the token was saved first.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateService().CompleteAsync(TelegramUserId, "the-code", CancellationToken.None));

        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
        Assert.NotNull(user.ProtectedGithubToken);
    }

    private sealed class FakeGitHubOAuthService : IGitHubOAuthService
    {
        public GitHubAuthorization Authorization { get; set; } = new("token", "login");

        public Exception? FailWith { get; set; }

        public string? ExchangedCode { get; private set; }

        public Uri BuildAuthorizationUrl(string state) => new($"https://github.com/login/oauth/authorize?state={state}");

        public Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException<GitHubAuthorization>(FailWith);
            }

            ExchangedCode = code;

            return Task.FromResult(Authorization);
        }

        public Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
