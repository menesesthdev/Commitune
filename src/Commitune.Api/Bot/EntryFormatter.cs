using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Commitune.Infrastructure.GitHub;

namespace Commitune.Api.Bot;

/// <summary>
/// Turns a message into the TIL file it becomes.
///
/// The convention is deliberately thin, because it has to be learnable from one example inside
/// a chat: <b>the first line is the title</b>, the rest is the body, and any <c>#tag</c>
/// becomes a tag. Nothing is mandatory — a one-line message is a valid entry, and a message
/// with no tags is a valid entry. Anything stricter would turn the bot into a form, which is
/// the one thing this product exists to avoid.
/// </summary>
public static partial class EntryFormatter
{
    /// <summary>
    /// The clock the user sees. Brazil dropped daylight saving in 2019, so a fixed −03:00 is
    /// correct all year and — unlike <c>TimeZoneInfo</c> — needs no tzdata in the container.
    /// TODO: store a timezone per user once the bot has anyone outside this offset.
    /// </summary>
    public static readonly TimeSpan DisplayOffset = TimeSpan.FromHours(-3);

    /// <summary>Long enough to say something, short enough to stay readable in a file listing.</summary>
    public const int MaxTitleLength = 80;

    private const int MaxSlugLength = 60;

    /// <summary>Git convention: a subject line that fits without being wrapped or elided.</summary>
    private const int MaxSubjectLength = 72;

    /// <summary>When there is no letter or digit to build a name from — an entry of pure emoji.</summary>
    private const string FallbackSlug = "til";

    public static TilEntry Format(DateTimeOffset timestamp, string text)
    {
        // An entry written at 23:40 belongs to that day, not to the UTC day it falls in.
        var local = timestamp.ToOffset(DisplayOffset);

        var (message, tags) = ExtractTags(text);
        var (title, body) = SplitTitleAndBody(message);

        var day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new TilEntry(
            PathPrefix: $"til/{day}-{Slugify(title)}",
            Title: title,
            Tags: tags,
            Content: BuildContent(title, body, tags, local),
            CommitMessage: BuildSubject(title));
    }

    /// <summary>
    /// A YAML frontmatter block, then the entry. The frontmatter is what makes the repository
    /// something a static site or a script can read later; the heading is what makes the file
    /// readable on GitHub itself, which is where anyone actually looks at it.
    /// </summary>
    private static string BuildContent(string title, string body, IReadOnlyList<string> tags, DateTimeOffset local)
    {
        var content = new StringBuilder();

        content.Append("---\n");
        content.Append($"title: {QuoteForYaml(title)}\n");
        // Full timestamp rather than a bare date: it still reads as a date to every static site
        // generator, and it keeps two entries from the same day in the order they were written.
        content.Append($"date: {local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)}\n");
        content.Append($"tags: [{string.Join(", ", tags)}]\n");
        content.Append("---\n\n");
        content.Append($"# {title}\n");

        if (body.Length > 0)
        {
            content.Append($"\n{body}\n");
        }

        return content.ToString();
    }

    /// <summary>
    /// The subject carries the title, unlike the entry body: a learning log whose history reads
    /// <c>Entrada de 18/08</c> is worth nothing to the person scrolling it a year from now.
    /// </summary>
    private static string BuildSubject(string title)
    {
        const string prefix = "TIL: ";

        return prefix + Truncate(title, MaxSubjectLength - prefix.Length);
    }

    /// <summary>
    /// Pulls <c>#tags</c> out of the message and hands back what is left of it. A tag has to
    /// start with a letter, so <c>#1</c> in "erro #1" stays part of the sentence.
    /// </summary>
    private static (string Message, IReadOnlyList<string> Tags) ExtractTags(string text)
    {
        var matches = TagPattern().Matches(text);

        if (matches.Count == 0)
        {
            return (text, []);
        }

        var tags = new List<string>();

        foreach (Match match in matches)
        {
            // A tag with nothing ASCII in it (#日本語) slugifies to nothing; it is dropped
            // rather than named after the fallback, which would tag half the repository alike.
            if (SlugifyOrEmpty(match.Groups[1].Value) is { Length: > 0 } tag && !tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        // The tags are metadata now; leaving them in the prose would repeat them in the title.
        var message = TagPattern().Replace(text, string.Empty);

        return (CollapseBlankSpace(message), tags);
    }

    /// <summary>
    /// First line is the title, the rest is the body. A first line too long to be a title is
    /// kept as body in full and elided into one — nothing the user wrote is dropped.
    /// </summary>
    private static (string Title, string Body) SplitTitleAndBody(string message)
    {
        var normalized = message.Replace("\r\n", "\n").Trim();

        var split = normalized.IndexOf('\n');
        var firstLine = (split < 0 ? normalized : normalized[..split]).Trim();

        if (firstLine.Length > MaxTitleLength)
        {
            return (Truncate(firstLine, MaxTitleLength), normalized);
        }

        var body = split < 0 ? string.Empty : normalized[(split + 1)..].Trim();

        return (firstLine, body);
    }

    /// <summary>Cuts on a word boundary when there is one close enough to the limit.</summary>
    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        // One character of the budget goes to the ellipsis that marks the cut.
        var cut = text[..(maxLength - 1)].TrimEnd();
        var lastSpace = cut.LastIndexOf(' ');

        if (lastSpace > maxLength / 2)
        {
            cut = cut[..lastSpace];
        }

        return $"{cut.TrimEnd(' ', ',', ';', ':', '.', '-')}…";
    }

    /// <summary>ASCII, lowercase, hyphen-separated — a file name that survives any checkout.</summary>
    private static string Slugify(string text)
        => SlugifyOrEmpty(text) is { Length: > 0 } slug ? slug : FallbackSlug;

    private static string SlugifyOrEmpty(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (Accents.Fold(character) is { } folded)
            {
                builder.Append(folded);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxSlugLength)
        {
            var cut = slug[..MaxSlugLength];
            var lastDash = cut.LastIndexOf('-');

            slug = (lastDash > MaxSlugLength / 2 ? cut[..lastDash] : cut).Trim('-');
        }

        return slug;
    }

    /// <summary>YAML double-quoted style: the one form that survives colons, quotes and hashes.</summary>
    private static string QuoteForYaml(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// Cleans up after the tags: the spaces they left behind, and the lines they left empty.
    /// </summary>
    private static string CollapseBlankSpace(string text)
        => BlankLinePattern().Replace(RepeatedSpacePattern().Replace(text, " "), "\n\n");

    [GeneratedRegex(@"(?<=^|\s)#(\p{L}[\p{L}\p{N}_-]*)")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex RepeatedSpacePattern();

    [GeneratedRegex(@"[ \t]*\n[ \t]*(\n[ \t]*)+")]
    private static partial Regex BlankLinePattern();
}
