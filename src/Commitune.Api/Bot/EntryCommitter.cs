using System.Security.Cryptography;
using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Octokit;

// Octokit has a RepositoryReference of its own; ours is the one meant here.
using RepositoryReference = Commitune.Infrastructure.GitHub.RepositoryReference;

namespace Commitune.Api.Bot;

public sealed class EntryCommitter(
    IBotUserStore users,
    IGitHubRepositoryService repositories,
    ITokenProtector tokenProtector,
    TimeProvider timeProvider,
    ILogger<EntryCommitter> logger) : IEntryCommitter
{
    public async Task<EntryCommitResult> CommitAsync(
        BotUser user,
        string text,
        CancellationToken cancellationToken)
    {
        if (user.RepositoryOwner is not { Length: > 0 } owner
            || user.RepositoryName is not { Length: > 0 } name)
        {
            // Ready without a repository should not happen; if it does, asking for a name is
            // the only way out that does not lose the message silently.
            logger.LogWarning("User {TelegramUserId} is Ready with no repository recorded.", user.TelegramUserId);

            return await AskForARepositoryAsync(user, cancellationToken);
        }

        if (user.ProtectedGithubToken is not { Length: > 0 } protectedToken)
        {
            return await ExpireAuthorizationAsync(user, cancellationToken);
        }

        string accessToken;
        try
        {
            accessToken = tokenProtector.Unprotect(protectedToken);
        }
        catch (CryptographicException exception)
        {
            // Key ring lost or rotated away — the stored token is unreadable, not invalid.
            logger.LogError(exception, "Could not decrypt the stored token for user {TelegramUserId}.", user.TelegramUserId);

            return await ExpireAuthorizationAsync(user, cancellationToken);
        }

        var entry = EntryFormatter.Format(timeProvider.GetUtcNow(), text);

        try
        {
            var committed = await repositories.CommitEntryAsync(
                accessToken, new RepositoryReference(owner, name), entry, cancellationToken);

            // The id and nothing else: the path is built from the user's own words now, so
            // logging it would put the entry itself in the server log.
            logger.LogInformation("Committed a TIL for user {TelegramUserId}.", user.TelegramUserId);

            return new EntryCommitResult(
                EntryCommitOutcome.Committed, committed.Url, entry.Title, entry.Tags);
        }
        catch (AuthorizationException)
        {
            // Token revoked on GitHub's side, or the grant expired.
            return await ExpireAuthorizationAsync(user, cancellationToken);
        }
        catch (NotFoundException)
        {
            // The repository was deleted or renamed on GitHub. The token still works, so the
            // way back is a new name — not a new authorization.
            logger.LogWarning("Repository gone for user {TelegramUserId}.", user.TelegramUserId);

            return await AskForARepositoryAsync(user, cancellationToken);
        }
        catch (Exception exception)
            when (exception is ApiException or HttpRequestException or TimeoutException
                or EntryPathUnavailableException)
        {
            // Rate limit, a 5xx from GitHub, a network blip. Nothing the user did, and nothing
            // they can fix — but they still hear about it instead of losing the message.
            logger.LogError(
                exception,
                "GitHub refused the commit for user {TelegramUserId} ({Status}).",
                user.TelegramUserId,
                (exception as ApiException)?.StatusCode);

            return new EntryCommitResult(EntryCommitOutcome.Failed);
        }
    }

    /// <summary>
    /// Drops the unusable token and sends the user back to the authorization step, so the
    /// reply can be "reconnect" instead of a dead end.
    /// </summary>
    private async Task<EntryCommitResult> ExpireAuthorizationAsync(BotUser user, CancellationToken cancellationToken)
    {
        user.ProtectedGithubToken = null;
        user.State = OnboardingState.AwaitingGithubAuth;
        await users.SaveAsync(user, cancellationToken);

        return new EntryCommitResult(EntryCommitOutcome.AuthorizationExpired);
    }

    /// <summary>
    /// Puts the user back on the "name your repository" step. The old reference is cleared so
    /// nothing keeps pointing at a repository that is not there.
    /// </summary>
    private async Task<EntryCommitResult> AskForARepositoryAsync(BotUser user, CancellationToken cancellationToken)
    {
        user.RepositoryOwner = null;
        user.RepositoryName = null;
        user.State = OnboardingState.AwaitingRepoName;
        await users.SaveAsync(user, cancellationToken);

        return new EntryCommitResult(EntryCommitOutcome.RepositoryMissing);
    }
}
