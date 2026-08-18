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
    /// Appends an entry to its day's file through the Contents API, creating the file when the
    /// day is new. Implementations must handle the <c>409</c> stale-sha conflict by re-fetching
    /// the file and retrying.
    /// </summary>
    Task<CommittedEntry> CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        DiaryEntry entry,
        CancellationToken cancellationToken);
}
