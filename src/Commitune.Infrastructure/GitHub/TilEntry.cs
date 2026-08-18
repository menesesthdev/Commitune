namespace Commitune.Infrastructure.GitHub;

/// <summary>
/// One message, already turned into the TIL file it becomes.
///
/// The path arrives as a prefix rather than a finished path because the final name is only
/// known against the repository: two entries about the same topic on the same day would land
/// on the same file, and the second one must get its own instead of overwriting the first.
/// </summary>
/// <param name="PathPrefix">Path without the extension, e.g. <c>til/2026-08-18-indice-parcial</c>.</param>
/// <param name="Title">Human title, as it appears in the frontmatter and the heading.</param>
/// <param name="Tags">Slugified tags, possibly empty, in the order the user wrote them.</param>
/// <param name="Content">The whole file: YAML frontmatter, heading, body.</param>
/// <param name="CommitMessage">Subject of the commit.</param>
public readonly record struct TilEntry(
    string PathPrefix,
    string Title,
    IReadOnlyList<string> Tags,
    string Content,
    string CommitMessage);

/// <summary>What the commit produced, so the bot can point the user at it.</summary>
/// <param name="CommitSha">Sha of the commit GitHub created.</param>
/// <param name="Path">Path the entry actually landed on, once collisions were resolved.</param>
/// <param name="Url">The file on github.com, when GitHub returned one.</param>
public readonly record struct CommittedEntry(string CommitSha, string Path, Uri? Url);

/// <summary>
/// Every candidate path for an entry was already taken. Distinct from a GitHub failure: there
/// is nothing to retry and nothing the user did wrong, but they still have to be told.
/// </summary>
public sealed class EntryPathUnavailableException(string pathPrefix, int attempts)
    : Exception($"No free path for '{pathPrefix}' after {attempts} attempts.")
{
    public string PathPrefix { get; } = pathPrefix;
}
