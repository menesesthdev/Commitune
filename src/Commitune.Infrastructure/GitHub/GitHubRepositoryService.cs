using Octokit;

namespace Commitune.Infrastructure.GitHub;

public sealed class GitHubRepositoryService : IGitHubRepositoryService
{
    /// <summary>User-Agent GitHub sees. Required by the API.</summary>
    public static readonly ProductHeaderValue Product = new("Commitune");

    public Task<RepositoryReference> CreatePrivateRepositoryAsync(
        string accessToken,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        // TODO: create the repository with Octokit:
        //   var repo = await client.Repository.Create(new NewRepository(repositoryName)
        //   {
        //       Private = true,   // NON-NEGOTIABLE — POST /user/repos must always send private: true
        //       AutoInit = true,  // gives the Contents API a base commit to write against
        //   });
        // and return new RepositoryReference(repo.Owner.Login, repo.Name).
        throw new NotImplementedException("Repository creation is not wired up yet.");
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

    /// <summary>Builds a client bound to a single user's token, for the duration of one request.</summary>
    private static GitHubClient CreateClient(string accessToken)
        => new(Product) { Credentials = new Credentials(accessToken) };
}
