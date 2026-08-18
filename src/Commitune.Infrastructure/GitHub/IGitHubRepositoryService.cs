namespace Commitune.Infrastructure.GitHub;

public interface IGitHubRepositoryService
{
    /// <summary>
    /// Creates the user's repository. There is deliberately no "visibility" parameter:
    /// Commitune only ever creates private repositories, so the call site cannot get it wrong.
    /// </summary>
    Task<RepositoryReference> CreatePrivateRepositoryAsync(
        string accessToken,
        string repositoryName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an entry as a new file through the Contents API. Implementations must never
    /// overwrite an existing file: a taken path means another entry is already there, and this
    /// one gets a name of its own — including when the collision only shows up as a
    /// <c>409</c>/<c>422</c> because a concurrent write took the path first.
    /// </summary>
    /// <exception cref="EntryPathUnavailableException">Every candidate path was taken.</exception>
    Task<CommittedEntry> CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        TilEntry entry,
        CancellationToken cancellationToken);
}
