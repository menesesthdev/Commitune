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
    IGitHubOAuthService gitHubOAuth) : IConversationHandler
{
    public Task HandleAsync(BotUser user, string text, CancellationToken cancellationToken)
    {
        var command = ParseCommand(text);

        return command switch
        {
            "/start" => HandleStartAsync(user, cancellationToken),
            "/pausar" => HandlePauseAsync(user, cancellationToken),
            "/repo" or "/desconectar" => messenger.SendTextAsync(
                user.TelegramChatId, BotReplies.NotAvailableYet, cancellationToken),
            not null => messenger.SendTextAsync(
                user.TelegramChatId, BotReplies.UnknownCommand, cancellationToken),
            _ => HandleTextAsync(user, cancellationToken),
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
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.AskRepoName, cancellationToken);
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
    /// A plain message. Only a <see cref="OnboardingState.Ready"/> user is writing a diary
    /// entry — mid-onboarding the text belongs to the conversation and must never be committed.
    /// </summary>
    private async Task HandleTextAsync(BotUser user, CancellationToken cancellationToken)
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
                // The repository name. Creating it lands in the next slice.
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.ComingSoon, cancellationToken);
                break;

            case OnboardingState.Ready:
                // The commit pipeline lands in the next slice.
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.ComingSoon, cancellationToken);
                break;

            case OnboardingState.Paused:
                await messenger.SendTextAsync(user.TelegramChatId, BotReplies.PausedReminder, cancellationToken);
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
    /// Returns the normalized command ("/start"), or <c>null</c> when the text is not a command.
    /// Telegram appends <c>@botname</c> to commands sent in groups.
    /// </summary>
    private static string? ParseCommand(string text)
    {
        var trimmed = text.TrimStart();

        if (!trimmed.StartsWith('/'))
        {
            return null;
        }

        var token = trimmed.Split(' ', 2)[0];
        var at = token.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            token = token[..at];
        }

        return token.ToLowerInvariant();
    }
}
