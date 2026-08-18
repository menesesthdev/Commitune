using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;

namespace Commitune.Tests.Bot.Fakes;

public sealed class FakeEntryCommitter : IEntryCommitter
{
    public static readonly Uri EntryUrl =
        new("https://github.com/tester/til/blob/main/til/2026-08-18-indices-parciais.md");

    public const string Title = "Índices parciais";

    /// <summary>What the next call returns. Defaults to the happy path.</summary>
    public EntryCommitResult Result { get; set; } =
        new(EntryCommitOutcome.Committed, EntryUrl, Title, ["postgres"]);

    public string? CommittedText { get; private set; }

    public Task<EntryCommitResult> CommitAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        CommittedText = text;

        // Mirror the real committer's side effects, so the conversation tests see the state
        // the user would actually be left in.
        user.State = Result.Outcome switch
        {
            EntryCommitOutcome.AuthorizationExpired => OnboardingState.AwaitingGithubAuth,
            EntryCommitOutcome.RepositoryMissing => OnboardingState.AwaitingRepoName,
            _ => user.State,
        };

        return Task.FromResult(Result);
    }
}
