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

    [Fact]
    public async Task Reports_a_name_the_user_already_used()
    {
        var user = SeedAuthorizedUser();
        _repositories.FailWith = new RepositoryExistsException("diario", new ApiValidationException());

        var result = await CreateProvisioner().ProvisionAsync(user, "diario", CancellationToken.None);

        Assert.Equal(RepositoryProvisionOutcome.NameAlreadyTaken, result.Outcome);

        // Still owes us a name, so the state must not move.
        Assert.Equal(OnboardingState.AwaitingRepoName, user.State);
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

        public Task CommitEntryAsync(
            string accessToken,
            RepositoryReference repository,
            string path,
            string content,
            string commitMessage,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
