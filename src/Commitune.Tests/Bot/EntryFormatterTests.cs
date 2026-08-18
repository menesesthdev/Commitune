using Commitune.Api.Bot;

namespace Commitune.Tests.Bot;

public class EntryFormatterTests
{
    /// <summary>22:30 in São Paulo, already the next day in UTC.</summary>
    private static readonly DateTimeOffset LateNight = new(2026, 8, 19, 1, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The entry belongs to the day the user was living, not to the UTC day it fell in.
    /// Anything else dates an evening's learning as tomorrow.
    /// </summary>
    [Fact]
    public void Dates_a_late_night_entry_by_the_users_day()
    {
        var entry = EntryFormatter.Format(LateNight, "Índices parciais no Postgres");

        Assert.Equal("til/2026-08-18-indices-parciais-no-postgres", entry.PathPrefix);
        Assert.Contains("date: 2026-08-18T22:30:00-03:00", entry.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_line_is_the_title_and_the_rest_is_the_body()
    {
        var entry = EntryFormatter.Format(
            LateNight,
            "Índice não entra com função na coluna\nNo Postgres, WHERE lower(email) ignora o índice.");

        Assert.Equal("Índice não entra com função na coluna", entry.Title);
        Assert.Equal(
            """
            ---
            title: "Índice não entra com função na coluna"
            date: 2026-08-18T22:30:00-03:00
            tags: []
            ---

            # Índice não entra com função na coluna

            No Postgres, WHERE lower(email) ignora o índice.

            """,
            entry.Content.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_one_line_entry_is_a_whole_entry()
    {
        var entry = EntryFormatter.Format(LateNight, "TimeProvider existe desde o .NET 8");

        // No body: repeating the title underneath it would be noise in every short entry.
        Assert.EndsWith("# TimeProvider existe desde o .NET 8\n", entry.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Tags_come_out_of_the_message_and_into_the_frontmatter()
    {
        var entry = EntryFormatter.Format(LateNight, "Índice parcial #postgres #índices");

        Assert.Equal(["postgres", "indices"], entry.Tags);
        Assert.Contains("tags: [postgres, indices]", entry.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tag_is_metadata_so_it_leaves_the_prose()
    {
        var entry = EntryFormatter.Format(LateNight, "#postgres Índice parcial economiza espaço");

        Assert.Equal("Índice parcial economiza espaço", entry.Title);
        Assert.Equal("til/2026-08-18-indice-parcial-economiza-espaco", entry.PathPrefix);
    }

    [Fact]
    public void The_same_tag_written_twice_is_one_tag()
    {
        var entry = EntryFormatter.Format(LateNight, "Índices #postgres e mais índices #Postgres");

        Assert.Equal(["postgres"], entry.Tags);
    }

    /// <summary>
    /// A tag has to start with a letter, so numbering inside a sentence survives as prose.
    /// </summary>
    [Fact]
    public void A_number_after_a_hash_is_not_a_tag()
    {
        var entry = EntryFormatter.Format(LateNight, "Corrigi o erro #1 do backlog");

        Assert.Empty(entry.Tags);
        Assert.Equal("Corrigi o erro #1 do backlog", entry.Title);
    }

    [Fact]
    public void An_entry_without_tags_still_has_the_field()
    {
        // Predictable frontmatter beats pretty frontmatter: anything reading the repository
        // later gets the same shape from every file.
        var entry = EntryFormatter.Format(LateNight, "Sem tags aqui");

        Assert.Contains("tags: []", entry.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_first_line_becomes_a_short_title_without_losing_the_text()
    {
        var text =
            "Descobri que o EF Core traduz Any() para EXISTS em vez de COUNT, o que muda o plano " +
            "de execução inteiro em tabelas grandes";

        var entry = EntryFormatter.Format(LateNight, text);

        Assert.True(entry.Title.Length <= EntryFormatter.MaxTitleLength);
        Assert.EndsWith("…", entry.Title, StringComparison.Ordinal);

        // Elided in the title, intact in the body.
        Assert.Contains("tabelas grandes", entry.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Índice não entra", "til/2026-08-18-indice-nao-entra")]
    [InlineData("C# 14: field keyword", "til/2026-08-18-c-14-field-keyword")]
    [InlineData("   espaços    demais   ", "til/2026-08-18-espacos-demais")]
    [InlineData("🎉🎉🎉", "til/2026-08-18-til")]
    public void The_path_is_ascii_lowercase_and_hyphenated(string text, string expected)
        => Assert.Equal(expected, EntryFormatter.Format(LateNight, text).PathPrefix);

    /// <summary>
    /// The subject line is what a learning log looks like from the outside — on the profile, in
    /// a diff, a year later. "Entry of 18/08" would tell that reader nothing.
    /// </summary>
    [Fact]
    public void The_commit_subject_carries_the_title()
    {
        var entry = EntryFormatter.Format(LateNight, "Índices parciais no Postgres");

        Assert.Equal("TIL: Índices parciais no Postgres", entry.CommitMessage);
    }

    [Fact]
    public void The_commit_subject_stays_within_git_conventions()
    {
        var entry = EntryFormatter.Format(LateNight, new string('a', 200));

        Assert.True(entry.CommitMessage.Length <= 72, entry.CommitMessage);
    }

    /// <summary>
    /// A title with a quote or a colon in it is the classic way to write invalid YAML and only
    /// find out when something tries to parse the repository.
    /// </summary>
    [Fact]
    public void A_title_full_of_yaml_punctuation_is_quoted_and_escaped()
    {
        var entry = EntryFormatter.Format(LateNight, """O erro: "sha" wasn't supplied""");

        Assert.Contains(
            "title: \"O erro: \\\"sha\\\" wasn't supplied\"",
            entry.Content,
            StringComparison.Ordinal);
    }
}
