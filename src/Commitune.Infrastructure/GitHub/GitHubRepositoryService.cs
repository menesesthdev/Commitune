using Octokit;

namespace Commitune.Infrastructure.GitHub;

public sealed class GitHubRepositoryService(IGitHubClientFactory clientFactory) : IGitHubRepositoryService
{
    public async Task<RepositoryReference> CreatePrivateRepositoryAsync(
        string accessToken,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        var client = clientFactory.Create(accessToken);

        var repository = await client.Repository.Create(new NewRepository(repositoryName)
        {
            // NON-NEGOTIABLE — POST /user/repos must always send private: true. There is no
            // parameter, no configuration and no caller that can turn this off.
            Private = true,

            // Gives the Contents API a base commit to write entries against.
            AutoInit = true,

            Description = "Meu diário, escrito pelo Telegram via Commitune.",
        });

        return new RepositoryReference(repository.Owner.Login, repository.Name);
    }

    public Task CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        string path,
        string content,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        // TODO: Contents API create-or-update. Fetch the existing file to get its sha; on a
        // 409 (stale sha, two fast messages hitting the same file) re-fetch and retry once.
        throw new NotImplementedException("Committing entries is not wired up yet.");
    }
}
