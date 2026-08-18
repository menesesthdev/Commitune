namespace Commitune.Infrastructure.GitHub;

/// <summary>
/// One message, already turned into everything the Contents API needs to write it.
///
/// There are two contents rather than one because the entry is appended to a file that may or
/// may not exist yet — and, when a write races another one, the append has to be reapplied to
/// whatever the file holds at that moment. Handing over a single finished blob would make that
/// retry overwrite the entry that won the race.
/// </summary>
/// <param name="Path">Path in the repository, e.g. <c>diario/2026/08/2026-08-18.md</c>.</param>
/// <param name="NewFileContent">The whole file, for the first entry of the day.</param>
/// <param name="AppendedBlock">The block added to a day already started.</param>
/// <param name="CommitMessage">Subject of the commit. Never carries the entry's text.</param>
public readonly record struct DiaryEntry(
    string Path,
    string NewFileContent,
    string AppendedBlock,
    string CommitMessage);

/// <summary>What the commit produced, so the bot can point the user at it.</summary>
/// <param name="CommitSha">Sha of the commit GitHub created.</param>
/// <param name="Url">The file on github.com, when GitHub returned one.</param>
public readonly record struct CommittedEntry(string CommitSha, Uri? Url);
