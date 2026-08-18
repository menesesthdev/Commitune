using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Octokit;

// Octokit has a RepositoryReference of its own; ours is the one meant here.
using RepositoryReference = Commitune.Infrastructure.GitHub.RepositoryReference;

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

            await PointAtAsync(user, repository, cancellationToken);

            return new RepositoryProvisionResult(RepositoryProvisionOutcome.Created, repository);
        }
        catch (RepositoryExistsException)
        {
            return await AdoptAsync(user, accessToken, name, cancellationToken);
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
    /// The name is taken — by the user's own repository, most of the time. Pointing at it beats
    /// refusing: after <c>/desconectar</c> the old repository is still there, and "you already
    /// have one with that name" would be a wall the user cannot get past.
    /// </summary>
    private async Task<RepositoryProvisionResult> AdoptAsync(
        BotUser user,
        string accessToken,
        string name,
        CancellationToken cancellationToken)
    {
        if (user.GithubLogin is not { Length: > 0 } login)
        {
            // No login recorded, so there is no owner to look the repository up under.
            return new RepositoryProvisionResult(RepositoryProvisionOutcome.NameAlreadyTaken);
        }

        var existing = await repositories.FindRepositoryAsync(accessToken, login, name, cancellationToken);

        if (existing is not { } repository)
        {
            // Create said it exists, Get says it does not: the name belongs to something this
            // token cannot see. Nothing to adopt.
            return new RepositoryProvisionResult(RepositoryProvisionOutcome.NameAlreadyTaken);
        }

        if (!repository.IsPrivate)
        {
            // Entries are private by design. Publishing them is the user's decision to make on
            // GitHub, deliberately — never a side effect of typing a name here.
            return new RepositoryProvisionResult(
                RepositoryProvisionOutcome.ExistingIsPublic, repository.Reference);
        }

        await PointAtAsync(user, repository.Reference, cancellationToken);

        return new RepositoryProvisionResult(RepositoryProvisionOutcome.Adopted, repository.Reference);
    }

    /// <summary>Records the repository entries go to from now on and makes the user ready.</summary>
    private async Task PointAtAsync(BotUser user, RepositoryReference repository, CancellationToken cancellationToken)
    {
        user.RepositoryOwner = repository.Owner;
        user.RepositoryName = repository.Name;
        user.State = OnboardingState.Ready;

        await users.SaveAsync(user, cancellationToken);
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
