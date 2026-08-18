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

    /// <summary>
    /// The repository already existed, is private, and is now the one receiving entries.
    /// This is what makes a name reusable — after <c>/desconectar</c>, or when the user built
    /// the repository by hand, "it already exists" is an answer, not a dead end.
    /// </summary>
    Adopted,

    /// <summary>
    /// The repository exists but is public. Commitune does not write entries to a public
    /// repository, and making one private is the user's decision to take on GitHub.
    /// </summary>
    ExistingIsPublic,

    /// <summary>GitHub would not accept this name.</summary>
    InvalidName,

    /// <summary>The name is taken by something we cannot adopt (an organization's, say).</summary>
    NameAlreadyTaken,

    /// <summary>The stored token no longer works — the user has to reconnect.</summary>
    AuthorizationExpired,
}

public readonly record struct RepositoryProvisionResult(
    RepositoryProvisionOutcome Outcome,
    RepositoryReference? Repository = null,
    string? Suggestion = null);
