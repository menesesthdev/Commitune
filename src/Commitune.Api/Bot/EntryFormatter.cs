using System.Globalization;
using Commitune.Infrastructure.GitHub;

namespace Commitune.Api.Bot;

/// <summary>
/// Turns a message into the file it becomes. One file per day, so a day of writing reads as a
/// page instead of a pile of commits — and so the repository stays browsable a year in.
/// </summary>
public static class EntryFormatter
{
    /// <summary>
    /// The clock the user sees. Brazil dropped daylight saving in 2019, so a fixed −03:00 is
    /// correct all year and — unlike <c>TimeZoneInfo</c> — needs no tzdata in the container.
    /// TODO: store a timezone per user once the bot has anyone outside this offset.
    /// </summary>
    public static readonly TimeSpan DisplayOffset = TimeSpan.FromHours(-3);

    public static DiaryEntry Format(DateTimeOffset timestamp, string text)
    {
        // An entry written at 23:40 belongs to that day, not to the UTC day it falls in.
        var local = timestamp.ToOffset(DisplayOffset);

        var day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var readableDay = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var block = $"## {time}\n\n{text.Trim()}\n";

        return new DiaryEntry(
            Path: $"diario/{local.Year:D4}/{local.Month:D2}/{day}.md",
            NewFileContent: $"# {readableDay}\n\n{block}",
            AppendedBlock: block,
            // The message says when, never what: git history is not the place to duplicate the
            // entry, and a subject line built from user text is one paste away from garbage.
            CommitMessage: $"Entrada de {readableDay} às {time}");
    }
}
