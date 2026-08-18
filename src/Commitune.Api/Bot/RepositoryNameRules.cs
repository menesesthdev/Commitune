using System.Text;

namespace Commitune.Api.Bot;

/// <summary>
/// GitHub's rules for a repository name. Validated here rather than let GitHub reject it,
/// so the user gets a useful answer instead of an API error — and so a name that GitHub
/// would silently rewrite is never created behind the user's back.
/// </summary>
public static class RepositoryNameRules
{
    public const int MaxLength = 100;

    public static bool IsValid(string? name)
        => !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && name is not ("." or "..")
            && name.All(IsAllowed);

    /// <summary>
    /// Turns what the user typed into the closest legal name, for use as a suggestion.
    /// Never applied on its own — the user picks the name, so it is offered, not imposed.
    /// </summary>
    public static string? Suggest(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (IsAllowed(character))
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
            else if (Accents.Fold(character) is { } unaccented)
            {
                builder.Append(unaccented);
            }
        }

        var suggestion = builder.ToString().Trim('-');

        if (suggestion.Length > MaxLength)
        {
            suggestion = suggestion[..MaxLength].TrimEnd('-');
        }

        return IsValid(suggestion) ? suggestion : null;
    }

    private static bool IsAllowed(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
}
