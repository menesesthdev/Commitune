using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Commitune.Infrastructure.Telegram;

namespace Commitune.Api.Bot;

public sealed class ConversationHandler(
    IBotUserStore users,
    IBotMessenger messenger,
    IOAuthStateProtector stateProtector,
    IGitHubOAuthService gitHubOAuth,
    IRepositoryProvisioner repositoryProvisioner,
    IEntryCommitter entryCommitter,
    IAccountDisconnector accountDisconnector) : IConversationHandler
{
    public Task HandleAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        var (command, argument) = ParseCommand(text);

        return command switch
        {
            "/start" => HandleStartAsync(user, cancellationToken),
            "/pausar" => HandlePauseAsync(user, cancellationToken),
            "/repo" => HandleRepoAsync(user, argument, cancellationToken),
            "/desconectar" => HandleDisconnectAsync(user, cancellationToken),
            not null => messenger.SendTextAsync(
                user.TelegramChatId, BotReplies.UnknownCommand, cancellationToken),
            _ => HandleTextAsync(user, text, cancellationToken),
        };
    }

    /// <summary>
    /// <c>/start</c> is the one command that works from every state: it either begins
    /// onboarding, nudges the step still pending, or resumes a paused user.
    /// </summary>
    private async Task HandleStartAsync(BotUser user, CancellationToken cancellationToken)
    {
        switch (user.State)
        {
            case OnboardingState.NotStarted:
                user.State = OnboardingState.AwaitingGithubAuth;
                await users.SaveAsync(user, cancellationToken);
                await SendAuthorizationLinkAsync(user, BotReplies.Welcome, cancellationToken);
                break;

            case OnboardingState.AwaitingGithubAuth:
                // Idempotent on purpose: a second /start just reissues a fresh, unexpired link.
                await SendAuthorizationLinkAsync(user, BotReplies.ConnectAgain, cancellationToken);
                break;

            case OnboardingState.AwaitingRepoName:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.AskRepoNameAgain, cancellationToken);
                break;

            case OnboardingState.Ready:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.AlreadyReady, cancellationToken);
                break;

            case OnboardingState.Paused:
                user.State = OnboardingState.Ready;
                await users.SaveAsync(user, cancellationToken);
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.Resumed, cancellationToken);
                break;
        }
    }

    private async Task HandlePauseAsync(BotUser user, CancellationToken cancellationToken)
    {
        switch (user.State)
        {
            case OnboardingState.Ready:
                user.State = OnboardingState.Paused;
                await users.SaveAsync(user, cancellationToken);
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.Paused, cancellationToken);
                break;

            case OnboardingState.Paused:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.Paused, cancellationToken);
                break;

            default:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.NothingToPause, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// <c>/repo</c> takes the name as an argument (<c>/repo meu-til</c>) instead of asking for
    /// it in the next message. Asking would put a <c>Ready</c> user into
    /// <see cref="OnboardingState.AwaitingRepoName"/>, where the TIL they write next would be
    /// read as a repository name — and there would be no way back out without naming one.
    /// Bare <c>/repo</c> answers where entries are going today.
    /// </summary>
    private async Task HandleRepoAsync(BotUser user, string argument, CancellationToken cancellationToken)
    {
        switch (user.State)
        {
            case OnboardingState.NotStarted:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.StartFirst, cancellationToken);
                break;

            case OnboardingState.AwaitingGithubAuth:
                await SendAuthorizationLinkAsync(user, BotReplies.ConnectAgain, cancellationToken);
                break;

            case OnboardingState.AwaitingRepoName when argument.Length == 0:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.AskRepoNameAgain, cancellationToken);
                break;

            case OnboardingState.Ready or OnboardingState.Paused when argument.Length == 0:
                await messenger.SendTextAsync(user.TelegramChatId, RepositoryStatus(user), cancellationToken);
                break;

            default:
                await ProvisionRepositoryAsync(user, argument, cancellationToken);
                break;
        }
    }

    private static string RepositoryStatus(BotUser user)
        => user.RepositoryOwner is { Length: > 0 } owner && user.RepositoryName is { Length: > 0 } name
            ? BotReplies.RepositoryInUse(owner, name)
            // Ready without a repository recorded should not happen; asking for one beats
            // answering with a blank.
            : BotReplies.NoRepositoryYet;

    /// <summary>
    /// <c>/desconectar</c> works from every state and asks nothing back: the entries already
    /// written stay in the repository, so there is nothing here that a confirmation step would
    /// be protecting.
    /// </summary>
    private async Task HandleDisconnectAsync(BotUser user, CancellationToken cancellationToken)
    {
        var outcome = await accountDisconnector.DisconnectAsync(user, cancellationToken);

        var reply = outcome switch
        {
            DisconnectOutcome.Disconnected => BotReplies.Disconnected,
            DisconnectOutcome.DisconnectedWithoutRevoking => BotReplies.DisconnectedWithoutRevoking,
            _ => BotReplies.NothingToDisconnect,
        };

        await messenger.SendTextAsync(user.TelegramChatId, reply, cancellationToken);
    }

    /// <summary>
    /// A plain message. Only a <see cref="OnboardingState.Ready"/> user is writing a TIL
    /// entry — mid-onboarding the text belongs to the conversation and must never be committed.
    /// </summary>
    private async Task HandleTextAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        switch (user.State)
        {
            case OnboardingState.NotStarted:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.StartFirst, cancellationToken);
                break;

            case OnboardingState.AwaitingGithubAuth:
                await SendAuthorizationLinkAsync(user, BotReplies.FinishAuthFirst, cancellationToken);
                break;

            case OnboardingState.AwaitingRepoName:
                await ProvisionRepositoryAsync(user, text, cancellationToken);
                break;

            case OnboardingState.Ready:
                await CommitEntryAsync(user, text, cancellationToken);
                break;

            case OnboardingState.Paused:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.PausedReminder, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The text a user in <see cref="OnboardingState.AwaitingRepoName"/> sends is the name of
    /// their repository. Every outcome is answered — including the ones that leave the user
    /// where they were, so they know they still owe an answer.
    /// </summary>
    private async Task ProvisionRepositoryAsync(BotUser user, string requestedName, CancellationToken cancellationToken)
    {
        // Read before the provisioner moves the user: an answer given mid-onboarding has to
        // teach the convention, the same answer given later to /repo does not.
        var onboarding = user.State == OnboardingState.AwaitingRepoName;

        await messenger.SendTextAsync(user.TelegramChatId, BotReplies.CreatingRepo, cancellationToken);

        var result = await repositoryProvisioner.ProvisionAsync(user, requestedName, cancellationToken);

        switch (result.Outcome)
        {
            case RepositoryProvisionOutcome.Created:
            case RepositoryProvisionOutcome.Adopted:
                var repository = result.Repository!.Value;
                var created = result.Outcome == RepositoryProvisionOutcome.Created;
                await messenger.SendTextAsync(
                    user.TelegramChatId,
                    onboarding
                        ? BotReplies.RepoReady(repository.Owner, repository.Name, created)
                        : BotReplies.RepoSwitched(repository.Owner, repository.Name, created),
                    cancellationToken);
                break;

            case RepositoryProvisionOutcome.ExistingIsPublic:
                var publicRepository = result.Repository!.Value;
                await messenger.SendTextAsync(
                    user.TelegramChatId,
                    BotReplies.RepoIsPublic(publicRepository.Owner, publicRepository.Name),
                    cancellationToken);
                break;

            case RepositoryProvisionOutcome.InvalidName:
                await messenger.SendTextAsync(
                    user.TelegramChatId,
                    result.Suggestion is { Length: > 0 } suggestion
                        ? BotReplies.RepoNameInvalidWithSuggestion(suggestion)
                        : BotReplies.RepoNameInvalid,
                    cancellationToken);
                break;

            case RepositoryProvisionOutcome.NameAlreadyTaken:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.RepoNameTaken, cancellationToken);
                break;

            case RepositoryProvisionOutcome.AuthorizationExpired:
                // The provisioner already put the user back in AwaitingGithubAuth, so the
                // reply is the link itself rather than an instruction to go find it.
                await SendAuthorizationLinkAsync(user, BotReplies.AuthorizationExpired, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// The product itself: a message from a <see cref="OnboardingState.Ready"/> user becomes a
    /// TIL entry. Every outcome answers — a message swallowed in silence is the churn risk.
    /// </summary>
    private async Task CommitEntryAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        var result = await entryCommitter.CommitAsync(user, text, cancellationToken);

        switch (result.Outcome)
        {
            case EntryCommitOutcome.Committed:
                await messenger.SendTextAsync(
                    user.TelegramChatId,
                    BotReplies.EntryCommitted(result.Title, result.Tags, result.Url),
                    cancellationToken);
                break;

            case EntryCommitOutcome.AuthorizationExpired:
                // The committer already put the user back in AwaitingGithubAuth; the reply is
                // the reconnect link itself rather than an instruction to go find it.
                await SendAuthorizationLinkAsync(
                    user, BotReplies.AuthorizationExpiredWhileCommitting, cancellationToken);
                break;

            case EntryCommitOutcome.RepositoryMissing:
                // Back in AwaitingRepoName, so the next message is read as the new name.
                await messenger.SendTextAsync(
                    user.TelegramChatId, BotReplies.RepositoryMissing, cancellationToken);
                break;

            case EntryCommitOutcome.Failed:
                await messenger.SendTextAsync(
                    user.TelegramChatId, BotReplies.CommitFailed, cancellationToken);
                break;
        }
    }

    private Task SendAuthorizationLinkAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        // The state is signed and short-lived; the raw Telegram id never travels in the URL.
        var state = stateProtector.Create(user.TelegramUserId);

        return messenger.SendLinkAsync(
            user.TelegramChatId,
            text,
            BotReplies.ConnectButtonLabel,
            gitHubOAuth.BuildAuthorizationUrl(state),
            cancellationToken);
    }

    /// <summary>
    /// Splits a command into its normalized name ("/start") and whatever followed it — the
    /// repository name, for <c>/repo meu-til</c>. The name is <c>null</c> when the text is not
    /// a command at all. Telegram appends <c>@botname</c> to commands sent in groups.
    /// </summary>
    private static (string? Command, string Argument) ParseCommand(string text)
    {
        var trimmed = text.TrimStart();

        if (!trimmed.StartsWith('/'))
        {
            return (null, string.Empty);
        }

        var parts = trimmed.Split(' ', 2);

        var token = parts[0];
        var at = token.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            token = token[..at];
        }

        return (token.ToLowerInvariant(), parts.Length > 1 ? parts[1].Trim() : string.Empty);
    }
}
