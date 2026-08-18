using Commitune.Domain.Entities;

namespace Commitune.Api.Bot;

/// <summary>
/// Commits one message to the user's repository. Separate from the conversation for the same
/// reason as <see cref="IRepositoryProvisioner"/>: the GitHub failure modes are mapped once,
/// into outcomes the bot can explain to a user who cannot see them.
/// </summary>
public interface IEntryCommitter
{
    Task<EntryCommitResult> CommitAsync(BotUser user, string text, CancellationToken cancellationToken);
}

public enum EntryCommitOutcome
{
    /// <summary>The entry is in the repository.</summary>
    Committed,

    /// <summary>The stored token no longer works — the user has to reconnect.</summary>
    AuthorizationExpired,

    /// <summary>The repository is gone (deleted or renamed on GitHub). A new name is needed.</summary>
    RepositoryMissing,

    /// <summary>GitHub refused for a reason that may not repeat — worth trying again.</summary>
    Failed,
}

public readonly record struct EntryCommitResult(EntryCommitOutcome Outcome, Uri? Url = null);
