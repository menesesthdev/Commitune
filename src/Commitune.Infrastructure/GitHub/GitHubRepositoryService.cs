using System.Net;
using Octokit;

namespace Commitune.Infrastructure.GitHub;

public sealed class GitHubRepositoryService(IGitHubClientFactory clientFactory) : IGitHubRepositoryService
{
    /// <summary>
    /// How many names an entry may try before giving up. Two TILs about the same topic on the
    /// same day are normal; ten are a sign of something other than a name collision.
    /// </summary>
    public const int MaxPathAttempts = 10;

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

            Description = "O que eu aprendo, registrado pelo Telegram via Commitune.",
        });

        return new RepositoryReference(repository.Owner.Login, repository.Name);
    }

    public async Task<CommittedEntry> CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        TilEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.PathPrefix);

        var client = clientFactory.Create(accessToken);

        for (var attempt = 1; attempt <= MaxPathAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = CandidatePath(entry.PathPrefix, attempt);

            if (await ExistsAsync(client, repository, path))
            {
                continue;
            }

            try
            {
                var change = await client.Repository.Content.CreateFile(
                    repository.Owner,
                    repository.Name,
                    path,
                    new CreateFileRequest(entry.CommitMessage, entry.Content));

                return new CommittedEntry(
                    change.Commit.Sha,
                    path,
                    Uri.TryCreate(change.Content?.HtmlUrl, UriKind.Absolute, out var url) ? url : null);
            }
            catch (ApiException exception) when (IsWriteConflict(exception))
            {
                // Two messages sent seconds apart resolved to the same name: the path was free
                // when we looked and taken by the time we wrote. Nothing to merge — the next
                // name is free, and neither entry is lost.
            }
        }

        throw new EntryPathUnavailableException(entry.PathPrefix, MaxPathAttempts);
    }

    /// <summary>
    /// First entry of the day about a topic keeps the plain name; the ones after it are
    /// numbered, so a path is never reused and never overwritten.
    /// </summary>
    private static string CandidatePath(string pathPrefix, int attempt)
        => attempt == 1 ? $"{pathPrefix}.md" : $"{pathPrefix}-{attempt}.md";

    /// <remarks>
    /// A deleted repository answers 404 here too, and the two are indistinguishable at this
    /// point. Treating it as "free" is safe: the create that follows answers 404 as well, and
    /// the caller reads that as the repository being gone.
    /// </remarks>
    private static async Task<bool> ExistsAsync(
        IGitHubClient client,
        RepositoryReference repository,
        string path)
    {
        try
        {
            var contents = await client.Repository.Content.GetAllContents(
                repository.Owner, repository.Name, path);

            return contents.Count > 0;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// The two shapes GitHub uses to say "someone else wrote this path first": a stale sha
    /// (409) and a create over a file that now exists (422).
    /// </summary>
    private static bool IsWriteConflict(ApiException exception)
        => exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
}
