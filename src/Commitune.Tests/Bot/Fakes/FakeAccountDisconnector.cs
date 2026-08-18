using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;

namespace Commitune.Tests.Bot.Fakes;

public sealed class FakeAccountDisconnector : IAccountDisconnector
{
    /// <summary>What the next call returns. Defaults to the happy path.</summary>
    public DisconnectOutcome Outcome { get; set; } = DisconnectOutcome.Disconnected;

    public bool WasCalled { get; private set; }

    public Task<DisconnectOutcome> DisconnectAsync(BotUser user, CancellationToken cancellationToken)
    {
        WasCalled = true;

        // Mirror the real disconnector: every outcome leaves the user at the start.
        user.State = OnboardingState.NotStarted;
        user.ProtectedGithubToken = null;
        user.RepositoryOwner = null;
        user.RepositoryName = null;

        return Task.FromResult(Outcome);
    }
}
