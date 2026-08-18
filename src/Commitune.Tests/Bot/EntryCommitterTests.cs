using System.Net;
using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Security;
using Commitune.Tests.Bot.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Octokit;

// Octokit has a RepositoryReference of its own; ours is the one meant here.
using RepositoryReference = Commitune.Infrastructure.GitHub.RepositoryReference;

namespace Commitune.Tests.Bot;

public class EntryCommitterTests
{
    private const long TelegramUserId = 4242;
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private static readonly DateTimeOffset Now = new(2026, 8, 19, 1, 30, 0, TimeSpan.Zero);

    private readonly FakeBotUserStore _users = new();
    private readonly FakeGitHubRepositoryService _repositories = new();

    private readonly DataProtectionTokenProtector _tokenProtector =
        new(DataProtectionProvider.Create("commitune-tests"));

    private EntryCommitter CreateCommitter()
        => new(
            _users,
            _repositories,
            _tokenProtector,
            new FixedTimeProvider(Now),
            NullLogger<EntryCommitter>.Instance);

    private BotUser SeedReadyUser()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.Ready);
        user.ProtectedGithubToken = _tokenProtector.Protect(AccessToken);
        user.RepositoryOwner = "tester";
        user.RepositoryName = "til";

        return user;
    }

    [Fact]
    public async Task Commits_the_message_and_hands_back_the_link()
    {
        var user = SeedReadyUser();

        var result = await CreateCommitter().CommitAsync(user, "hoje eu escrevi um bot", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.Committed, result.Outcome);
        Assert.Equal(FakeGitHubRepositoryService.EntryUrl, result.Url);
        Assert.Equal(OnboardingState.Ready, user.State);
    }

    /// <summary>
    /// The bot inferred a title and tags from free-form text; the conversation needs both to
    /// show the user what it understood.
    /// </summary>
    [Fact]
    public async Task Reports_back_what_it_understood_from_the_message()
    {
        var user = SeedReadyUser();

        var result = await CreateCommitter().CommitAsync(
            user, "Índices parciais no Postgres #postgres", CancellationToken.None);

        Assert.Equal("Índices parciais no Postgres", result.Title);
        Assert.Equal(["postgres"], result.Tags);
    }

    [Fact]
    public async Task Decrypts_the_stored_token_before_calling_github()
    {
        var user = SeedReadyUser();

        await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(AccessToken, _repositories.UsedAccessToken);
    }

    [Fact]
    public async Task Writes_to_the_repository_recorded_for_this_user()
    {
        var user = SeedReadyUser();

        await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(new RepositoryReference("tester", "til"), _repositories.UsedRepository);
    }

    [Fact]
    public async Task Dates_the_entry_by_the_clock_it_was_given()
    {
        var user = SeedReadyUser();

        await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal("til/2026-08-18-uma-anotacao", _repositories.WrittenEntry.PathPrefix);
        Assert.Contains("uma anotação", _repositories.WrittenEntry.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A revoked token must not leave the user typing into a void — they go back to the
    /// authorization step, without the dead token, and the reply can be the reconnect link.
    /// </summary>
    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_the_token_no_longer_works()
    {
        var user = SeedReadyUser();
        _repositories.FailWith = new AuthorizationException();

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(user.ProtectedGithubToken);
    }

    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_there_is_no_token_at_all()
    {
        var user = SeedReadyUser();
        user.ProtectedGithubToken = null;

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(_repositories.UsedAccessToken);
    }

    /// <summary>
    /// If the Data Protection key ring is lost, stored tokens become undecryptable. That must
    /// read as "reconnect", not as an unhandled crash.
    /// </summary>
    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_the_token_cannot_be_decrypted()
    {
        var user = SeedReadyUser();
        user.ProtectedGithubToken = new DataProtectionTokenProtector(
            DataProtectionProvider.Create("a-lost-key-ring")).Protect(AccessToken);

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(user.ProtectedGithubToken);
    }

    /// <summary>
    /// The repository was deleted or renamed on GitHub. The token still works, so the way out
    /// is a new name — not a new authorization.
    /// </summary>
    [Fact]
    public async Task Asks_for_a_new_repository_when_the_old_one_is_gone()
    {
        var user = SeedReadyUser();
        _repositories.FailWith = new NotFoundException("gone", HttpStatusCode.NotFound);

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.RepositoryMissing, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
        Assert.Null(user.RepositoryName);
        Assert.NotNull(user.ProtectedGithubToken);
    }

    [Fact]
    public async Task Asks_for_a_repository_when_none_was_ever_recorded()
    {
        var user = SeedReadyUser();
        user.RepositoryOwner = null;
        user.RepositoryName = null;

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.RepositoryMissing, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
        Assert.Null(_repositories.UsedAccessToken);
    }

    /// <summary>
    /// A rate limit or a 5xx is nobody's fault and may well work in a minute. The user stays
    /// exactly where they are, so the next message is still an entry.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_refusal_from_github_leaves_the_user_ready_to_try_again(HttpStatusCode status)
    {
        var user = SeedReadyUser();
        _repositories.FailWith = new ApiException("recusado", status);

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.Failed, result.Outcome);
        Assert.Equal(OnboardingState.Ready, user.State);
        Assert.NotNull(user.ProtectedGithubToken);
        Assert.Equal("til", user.RepositoryName);
    }

    [Fact]
    public async Task A_network_failure_is_reported_rather_than_thrown_at_the_user()
    {
        var user = SeedReadyUser();
        _repositories.FailWith = new HttpRequestException("a rede caiu");

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.Failed, result.Outcome);
    }

    /// <summary>
    /// Running out of names is not a GitHub failure, but the user still has to hear that the
    /// entry did not land — the reply is the same, and the silence is what we refuse.
    /// </summary>
    [Fact]
    public async Task Running_out_of_names_is_reported_too()
    {
        var user = SeedReadyUser();
        _repositories.FailWith = new EntryPathUnavailableException("til/2026-08-18-uma-anotacao", 10);

        var result = await CreateCommitter().CommitAsync(user, "uma anotação", CancellationToken.None);

        Assert.Equal(EntryCommitOutcome.Failed, result.Outcome);
        Assert.Equal(OnboardingState.Ready, user.State);
    }

    private sealed class FakeGitHubRepositoryService : IGitHubRepositoryService
    {
        public const string EntryPath = "til/2026-08-18-uma-anotacao.md";

        public static readonly Uri EntryUrl = new($"https://github.com/tester/til/blob/main/{EntryPath}");

        public string? UsedAccessToken { get; private set; }

        public RepositoryReference UsedRepository { get; private set; }

        public TilEntry WrittenEntry { get; private set; }

        public Exception? FailWith { get; set; }

        public Task<RepositoryReference> CreatePrivateRepositoryAsync(
            string accessToken,
            string repositoryName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ExistingRepository?> FindRepositoryAsync(
            string accessToken,
            string owner,
            string repositoryName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommittedEntry> CommitEntryAsync(
            string accessToken,
            RepositoryReference repository,
            TilEntry entry,
            CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException<CommittedEntry>(FailWith);
            }

            UsedAccessToken = accessToken;
            UsedRepository = repository;
            WrittenEntry = entry;

            return Task.FromResult(new CommittedEntry("c0ffee", EntryPath, EntryUrl));
        }
    }
}
