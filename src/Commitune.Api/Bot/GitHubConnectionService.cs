using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Commitune.Infrastructure.Telegram;

namespace Commitune.Api.Bot;

public sealed class GitHubConnectionService(
    IBotUserStore users,
    IGitHubOAuthService gitHubOAuth,
    ITokenProtector tokenProtector,
    IBotMessenger messenger,
    ILogger<GitHubConnectionService> logger) : IGitHubConnectionService
{
    public async Task<GitHubConnectionOutcome> CompleteAsync(
        long telegramUserId,
        string code,
        CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(telegramUserId, cancellationToken);
        if (user is null)
        {
            // The state was signed by us, so this means the row vanished between /start and
            // the callback. Nothing to attach the authorization to.
            logger.LogWarning("Valid OAuth state for unknown Telegram user {TelegramUserId}.", telegramUserId);
            return GitHubConnectionOutcome.UnknownUser;
        }

        GitHubAuthorization authorization;
        try
        {
            authorization = await gitHubOAuth.ExchangeCodeAsync(code, cancellationToken);
        }
        catch (GitHubOAuthException exception)
        {
            // GitHubOAuthException carries an error code, never the response body.
            logger.LogWarning(
                exception,
                "Code exchange failed for Telegram user {TelegramUserId}.",
                telegramUserId);

            await NotifyFailureAsync(user, cancellationToken);
            return GitHubConnectionOutcome.Failed;
        }

        // The plaintext token lives only in this scope: protected here, discarded on return.
        user.ProtectedGithubToken = tokenProtector.Protect(authorization.AccessToken);
        user.GithubLogin = authorization.Login;

        var alreadyOnboarded = user.RepositoryName is { Length: > 0 };
        if (!alreadyOnboarded)
        {
            user.State = OnboardingState.AwaitingRepoName;
        }

        await users.SaveAsync(user, cancellationToken);

        // Saved before the message goes out: if Telegram is down, the authorization survives
        // and /start re-asks the question, instead of the user having to authorize again.
        await messenger.SendTextAsync(
            user.TelegramChatId,
            alreadyOnboarded ? BotReplies.Reconnected : BotReplies.AskRepoName,
            cancellationToken);

        return alreadyOnboarded
            ? GitHubConnectionOutcome.Reconnected
            : GitHubConnectionOutcome.AwaitingRepoName;
    }

    private async Task NotifyFailureAsync(BotUser user, CancellationToken cancellationToken)
    {
        try
        {
            await messenger.SendTextAsync(
                user.TelegramChatId, BotReplies.AuthorizationFailed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not tell chat {ChatId} that the authorization failed.", user.TelegramChatId);
        }
    }
}
