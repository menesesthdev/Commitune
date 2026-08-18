using System.Security.Cryptography;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;

namespace Commitune.Api.Bot;

public sealed class AccountDisconnector(
    IBotUserStore users,
    IGitHubOAuthService gitHubOAuth,
    ITokenProtector tokenProtector,
    ILogger<AccountDisconnector> logger) : IAccountDisconnector
{
    public async Task<DisconnectOutcome> DisconnectAsync(BotUser user, CancellationToken cancellationToken)
    {
        if (user.ProtectedGithubToken is not { Length: > 0 } protectedToken)
        {
            // Nothing stored. Still wipe the rest, in case a repository reference outlived the
            // token, and still put the user back at the start.
            await WipeAsync(user, cancellationToken);

            return DisconnectOutcome.NothingToDisconnect;
        }

        var revoked = await TryRevokeAsync(user, protectedToken, cancellationToken);

        // Wiped whatever GitHub answered: the user asked to be disconnected, and keeping a
        // token here because a remote call failed would be the opposite of what they asked for.
        await WipeAsync(user, cancellationToken);

        return revoked ? DisconnectOutcome.Disconnected : DisconnectOutcome.DisconnectedWithoutRevoking;
    }

    private async Task<bool> TryRevokeAsync(
        BotUser user,
        string protectedToken,
        CancellationToken cancellationToken)
    {
        string accessToken;
        try
        {
            accessToken = tokenProtector.Unprotect(protectedToken);
        }
        catch (CryptographicException exception)
        {
            // Unreadable token: nothing to revoke with, and nothing the user can do about it.
            logger.LogError(exception, "Could not decrypt the stored token for user {TelegramUserId}.", user.TelegramUserId);

            return false;
        }

        try
        {
            await gitHubOAuth.RevokeAsync(accessToken, cancellationToken);

            return true;
        }
        catch (Exception exception)
            when (exception is GitHubOAuthException or HttpRequestException or TimeoutException)
        {
            // GitHubOAuthException carries an error code, never the token or the response body.
            logger.LogWarning(exception, "Could not revoke the grant for user {TelegramUserId}.", user.TelegramUserId);

            return false;
        }
    }

    /// <summary>
    /// Leaves nothing about the GitHub account behind: no token, no login, no repository. The
    /// row itself stays — it is the Telegram conversation, which the user did not disconnect.
    /// </summary>
    private async Task WipeAsync(BotUser user, CancellationToken cancellationToken)
    {
        user.ProtectedGithubToken = null;
        user.GithubLogin = null;
        user.RepositoryOwner = null;
        user.RepositoryName = null;
        user.State = OnboardingState.NotStarted;

        await users.SaveAsync(user, cancellationToken);
    }
}
