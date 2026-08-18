namespace Commitune.Domain.Onboarding;

/// <summary>
/// Where a user is in the onboarding conversation.
///
/// NotStarted → AwaitingGithubAuth → AwaitingRepoName → Ready ⇄ Paused
///
/// While in <see cref="AwaitingGithubAuth"/> or <see cref="AwaitingRepoName"/>, incoming
/// messages belong to the onboarding conversation and must never be committed as entries.
/// </summary>
public enum OnboardingState
{
    NotStarted = 0,
    AwaitingGithubAuth = 1,
    AwaitingRepoName = 2,
    Ready = 3,
    Paused = 4,
}
