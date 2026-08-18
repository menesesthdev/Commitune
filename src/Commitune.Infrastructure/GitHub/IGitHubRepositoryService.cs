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
    /// Writes an entry through the Contents API. Implementations must handle the
    /// <c>409</c> stale-sha conflict by re-fetching the file and retrying.
    /// </summary>
    Task CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        string path,
        string content,
        string commitMessage,
        CancellationToken cancellationToken);
}
