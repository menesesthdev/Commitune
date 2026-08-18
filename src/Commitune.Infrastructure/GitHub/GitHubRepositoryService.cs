using System.Net;
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

    public async Task<CommittedEntry> CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        DiaryEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Path);

        var client = clientFactory.Create(accessToken);

        try
        {
            return await WriteAsync(client, repository, entry, cancellationToken);
        }
        catch (Exception exception) when (IsWriteConflict(exception))
        {
            // Two messages sent seconds apart land on the same day's file: the sha we read is
            // already stale by the time we write (409), or the file we thought was missing now
            // exists (422). Re-reading picks up the entry that won, and the append is applied
            // on top of it instead of replacing it. One retry — a second conflict is a signal
            // that something other than a race is wrong, and the user gets told.
            cancellationToken.ThrowIfCancellationRequested();

            return await WriteAsync(client, repository, entry, cancellationToken);
        }
    }

    private static async Task<CommittedEntry> WriteAsync(
        IGitHubClient client,
        RepositoryReference repository,
        DiaryEntry entry,
        CancellationToken cancellationToken)
    {
        var existing = await FindFileAsync(client, repository, entry.Path);

        cancellationToken.ThrowIfCancellationRequested();

        var change = existing is null
            ? await client.Repository.Content.CreateFile(
                repository.Owner,
                repository.Name,
                entry.Path,
                new CreateFileRequest(entry.CommitMessage, entry.NewFileContent))
            : await client.Repository.Content.UpdateFile(
                repository.Owner,
                repository.Name,
                entry.Path,
                new UpdateFileRequest(
                    entry.CommitMessage,
                    Append(existing.Content, entry.AppendedBlock),
                    existing.Sha));

        return new CommittedEntry(
            change.Commit.Sha,
            Uri.TryCreate(change.Content?.HtmlUrl, UriKind.Absolute, out var url) ? url : null);
    }

    /// <summary>
    /// The day's file, or <c>null</c> when this is the first entry of the day.
    /// </summary>
    /// <remarks>
    /// A deleted repository answers 404 here too, and the two are indistinguishable at this
    /// point. Treating it as "new day" is safe: the create that follows answers 404 as well,
    /// and the caller reads that as the repository being gone.
    /// </remarks>
    private static async Task<RepositoryContent?> FindFileAsync(
        IGitHubClient client,
        RepositoryReference repository,
        string path)
    {
        try
        {
            var contents = await client.Repository.Content.GetAllContents(
                repository.Owner, repository.Name, path);

            var file = contents.FirstOrDefault();

            if (file is not null && file.Content is null)
            {
                // GitHub stops inlining content past 1 MB. Appending to what we can read would
                // silently truncate the day, so refuse instead.
                throw new InvalidOperationException(
                    $"The Contents API did not return the body of '{path}'; it is too large to append to.");
            }

            return file;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>Keeps exactly one blank line between the day's entries.</summary>
    private static string Append(string existing, string block)
        => $"{existing.TrimEnd('\n')}\n\n{block}";

    private static bool IsWriteConflict(Exception exception) => exception is ApiException
    {
        StatusCode: HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
    };
}
