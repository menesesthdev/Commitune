using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;

namespace Commitune.Tests.Bot.Fakes;

public sealed class FakeRepositoryProvisioner : IRepositoryProvisioner
{
    public static readonly RepositoryReference Repository = new("tester", "diario");

    /// <summary>What the next call returns. Defaults to the happy path.</summary>
    public RepositoryProvisionResult Result { get; set; } =
        new(RepositoryProvisionOutcome.Created, Repository);

    public string? RequestedName { get; private set; }

    public Task<RepositoryProvisionResult> ProvisionAsync(
        BotUser user,
        string requestedName,
        CancellationToken cancellationToken)
    {
        RequestedName = requestedName;

        // Mirror the real provisioner's side effects, so the conversation tests see the
        // state the user would actually be left in.
        user.State = Result.Outcome switch
        {
            RepositoryProvisionOutcome.Created or RepositoryProvisionOutcome.Adopted => OnboardingState.Ready,
            RepositoryProvisionOutcome.AuthorizationExpired => OnboardingState.AwaitingGithubAuth,
            _ => user.State,
        };

        return Task.FromResult(Result);
    }
}
