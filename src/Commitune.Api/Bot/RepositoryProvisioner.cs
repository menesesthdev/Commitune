using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Octokit;

namespace Commitune.Api.Bot;

public sealed class RepositoryProvisioner(
    IBotUserStore users,
    IGitHubRepositoryService repositories,
    ITokenProtector tokenProtector,
    ILogger<RepositoryProvisioner> logger) : IRepositoryProvisioner
{
    public async Task<RepositoryProvisionResult> ProvisionAsync(
        BotUser user,
        string requestedName,
        CancellationToken cancellationToken)
    {
        var name = requestedName.Trim();

        if (!RepositoryNameRules.IsValid(name))
        {
            return new RepositoryProvisionResult(
                RepositoryProvisionOutcome.InvalidName,
                Suggestion: RepositoryNameRules.Suggest(name));
        }

        if (user.ProtectedGithubToken is not { Length: > 0 } protectedToken)
        {
            // No token but sitting in AwaitingRepoName: nothing to do but reconnect.
            return await ExpireAuthorizationAsync(user, cancellationToken);
        }

        string accessToken;
        try
        {
            accessToken = tokenProtector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            // Key ring lost or rotated away — the stored token is unreadable, not invalid.
            logger.LogError(exception, "Could not decrypt the stored token for user {TelegramUserId}.", user.TelegramUserId);
            return await ExpireAuthorizationAsync(user, cancellationToken);
        }

        try
        {
            var repository = await repositories.CreatePrivateRepositoryAsync(
                accessToken, name, cancellationToken);

            user.RepositoryOwner = repository.Owner;
            user.RepositoryName = repository.Name;
            user.State = OnboardingState.Ready;
            await users.SaveAsync(user, cancellationToken);

            return new RepositoryProvisionResult(RepositoryProvisionOutcome.Created, repository);
        }
        catch (RepositoryExistsException)
        {
            return new RepositoryProvisionResult(RepositoryProvisionOutcome.NameAlreadyTaken);
        }
        catch (AuthorizationException)
        {
            // Token revoked on GitHub's side, or the grant expired.
            return await ExpireAuthorizationAsync(user, cancellationToken);
        }
        catch (ApiValidationException exception)
        {
            // GitHub rejected the name for a reason our rules do not cover (reserved words,
            // org policy). Never log the exception body — it echoes the request.
            logger.LogWarning(
                "GitHub rejected the repository name for user {TelegramUserId} ({Status}).",
                user.TelegramUserId,
                exception.HttpResponse?.StatusCode);

            return new RepositoryProvisionResult(RepositoryProvisionOutcome.InvalidName);
        }
    }

    /// <summary>
    /// Drops the unusable token and sends the user back to the authorization step, so the
    /// reply can be "reconnect" instead of a dead end.
    /// </summary>
    private async Task<RepositoryProvisionResult> ExpireAuthorizationAsync(
        BotUser user,
        CancellationToken cancellationToken)
    {
        user.ProtectedGithubToken = null;
        user.State = OnboardingState.AwaitingGithubAuth;
        await users.SaveAsync(user, cancellationToken);

        return new RepositoryProvisionResult(RepositoryProvisionOutcome.AuthorizationExpired);
    }
}
