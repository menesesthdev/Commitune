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

public class RepositoryProvisionerTests
{
    private const long TelegramUserId = 4242;
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private readonly FakeBotUserStore _users = new();
    private readonly FakeGitHubRepositoryService _repositories = new();

    private readonly DataProtectionTokenProtector _tokenProtector =
        new(DataProtectionProvider.Create("commitune-tests"));

    private RepositoryProvisioner CreateProvisioner()
        => new(_users, _repositories, _tokenProtector, NullLogger<RepositoryProvisioner>.Instance);

    private BotUser SeedAuthorizedUser()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingRepoName);
        user.ProtectedGithubToken = _tokenProtector.Protect(AccessToken);

        return user;
    }

    [Fact]
    public async Task Creates_the_repository_and_makes_the_user_ready()
    {
        var user = SeedAuthorizedUser();

        var result = await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.Created, result.Outcome);
        Assert.Equal(OnboardingState.Ready, user.State);
        Assert.Equal("tester", user.RepositoryOwner);
        Assert.Equal("diario", user.RepositoryName);
    }

    [Fact]
    public async Task Decrypts_the_stored_token_before_calling_github()
    {
        var user = SeedAuthorizedUser();

        await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(AccessToken, _repositories.UsedAccessToken);
    }

    [Fact]
    public async Task Trims_what_the_user_typed()
    {
        var user = SeedAuthorizedUser();

        await CreateProvisioner().ProvisionAsync(user, "  diario\n", CancellationToken.None);

        Assert.Equal("diario", _repositories.RequestedName);
    }

    [Theory]
    [InlineData("meu diario")]
    [InlineData("diário")]
    [InlineData("")]
    public async Task Rejects_an_illegal_name_without_calling_github(string name)
    {
        var user = SeedAuthorizedUser();

        var result = await CreateProvisioner().ProvisionAsync(user, name, CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.InvalidName, result.Outcome);
        Assert.Null(_repositories.RequestedName);
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
    }

    [Fact]
    public async Task Suggests_a_legal_name_when_one_can_be_salvaged()
    {
        var user = SeedAuthorizedUser();

        var result = await CreateProvisioner().ProvisionAsync(user, "Meu Diário", CancellationToken.None);

        Assert.Equal("meu-diario", result.Suggestion);
    }

    /// <summary>
    /// The name is taken by the user's own private repository — which is what happens after
    /// /desconectar, since the repository outlives the authorization. Refusing would leave the
    /// user unable to go back to the repository they have been writing in.
    /// </summary>
    [Fact]
    public async Task Adopts_a_private_repository_the_user_already_has()
    {
        var user = SeedAuthorizedUser();
        user.GithubLogin = "tester";
        _repositories.FailWith = new RepositoryExistsException("til", new ApiValidationException());
        _repositories.Existing = new ExistingRepository(new RepositoryReference("tester", "til"), IsPrivate: true);

        var result = await CreateProvisioner().ProvisionAsync(user, "til", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.Adopted, result.Outcome);
        Assert.Equal(OnboardingState.Ready, user.State);
        Assert.Equal("tester", user.RepositoryOwner);
        Assert.Equal("til", user.RepositoryName);
    }

    /// <summary>
    /// The rule that creation cannot break, applied to the one path that could get around it:
    /// a repository Commitune did not create is the only one whose visibility is not ours.
    /// </summary>
    [Fact]
    public async Task Refuses_to_write_into_a_public_repository()
    {
        var user = SeedAuthorizedUser();
        user.GithubLogin = "tester";
        _repositories.FailWith = new RepositoryExistsException("blog", new ApiValidationException());
        _repositories.Existing = new ExistingRepository(new RepositoryReference("tester", "blog"), IsPrivate: false);

        var result = await CreateProvisioner().ProvisionAsync(user, "blog", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.ExistingIsPublic, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
        Assert.Null(user.RepositoryName);
    }

    [Fact]
    public async Task Reports_a_name_taken_by_a_repository_it_cannot_see()
    {
        var user = SeedAuthorizedUser();
        user.GithubLogin = "tester";
        _repositories.FailWith = new RepositoryExistsException("til", new ApiValidationException());
        _repositories.Existing = null;

        var result = await CreateProvisioner().ProvisionAsync(user, "til", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.NameAlreadyTaken, result.Outcome);

        // Still owes us a name, so the state must not move.
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
    }

    [Fact]
    public async Task Cannot_adopt_without_knowing_which_account_to_look_under()
    {
        var user = SeedAuthorizedUser();
        user.GithubLogin = null;
        _repositories.FailWith = new RepositoryExistsException("til", new ApiValidationException());

        var result = await CreateProvisioner().ProvisionAsync(user, "til", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.NameAlreadyTaken, result.Outcome);
        Assert.Null(_repositories.LookedUpOwner);
    }

    /// <summary>
    /// A revoked token must not leave the user stuck answering a question that can never
    /// succeed — they go back to the authorization step, without the dead token.
    /// </summary>
    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_the_token_no_longer_works()
    {
        var user = SeedAuthorizedUser();
        _repositories.FailWith = new AuthorizationException();

        var result = await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(user.ProtectedGithubToken);
    }

    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_there_is_no_token_at_all()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingRepoName);

        var result = await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
    }

    /// <summary>
    /// If the Data Protection key ring is lost, stored tokens become undecryptable. That must
    /// read as "reconnect", not as an unhandled crash.
    /// </summary>
    [Fact]
    public async Task Sends_the_user_back_to_authorization_when_the_token_cannot_be_decrypted()
    {
        var user = _users.Seed(TelegramUserId, OnboardingState.AwaitingRepoName);
        user.ProtectedGithubToken = new DataProtectionTokenProtector(
            DataProtectionProvider.Create("a-lost-key-ring")).Protect(AccessToken);

        var result = await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.AuthorizationExpired, result.Outcome);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Null(user.ProtectedGithubToken);
    }

    private sealed class FakeGitHubRepositoryService : IGitHubRepositoryService
    {
        public string? RequestedName { get; private set; }

        public string? UsedAccessToken { get; private set; }

        public Exception? FailWith { get; set; }

        /// <summary>What the lookup finds when creation reports the name is taken.</summary>
        public ExistingRepository? Existing { get; set; }

        public string? LookedUpOwner { get; private set; }

        public Task<ExistingRepository?> FindRepositoryAsync(
            string accessToken,
            string owner,
            string repositoryName,
            CancellationToken cancellationToken)
        {
            UsedAccessToken = accessToken;
            LookedUpOwner = owner;

            return Task.FromResult(Existing);
        }

        public Task<RepositoryReference> CreatePrivateRepositoryAsync(
            string accessToken,
            string repositoryName,
            CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException<RepositoryReference>(FailWith);
            }

            UsedAccessToken = accessToken;
            RequestedName = repositoryName;

            return Task.FromResult(new RepositoryReference("tester", repositoryName));
        }

        public Task<CommittedEntry> CommitEntryAsync(
            string accessToken,
            RepositoryReference repository,
            TilEntry entry,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
