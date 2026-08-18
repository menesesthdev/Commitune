using Commitune.Domain.Entities;
using Commitune.Infrastructure.GitHub;

namespace Commitune.Api.Bot;

/// <summary>
/// Creates the user's repository and records it. Separate from the conversation so the
/// GitHub failure modes are mapped once, into outcomes the bot can actually explain.
/// </summary>
public interface IRepositoryProvisioner
{
    Task<RepositoryProvisionResult> ProvisionAsync(
        BotUser user,
        string requestedName,
        CancellationToken cancellationToken);
}

public enum RepositoryProvisionOutcome
{
    /// <summary>Created, private, and the user is now <c>Ready</c>.</summary>
    Created,

    /// <summary>GitHub would not accept this name.</summary>
    InvalidName,

    /// <summary>The user already has a repository with this name.</summary>
    NameAlreadyTaken,

    /// <summary>The stored token no longer works — the user has to reconnect.</summary>
    AuthorizationExpired,
}

public readonly record struct RepositoryProvisionResult(
    RepositoryProvisionOutcome Outcome,
    RepositoryReference? Repository = null,
    string? Suggestion = null);
