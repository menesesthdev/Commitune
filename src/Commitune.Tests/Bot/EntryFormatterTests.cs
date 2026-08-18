using Commitune.Api.Bot;

namespace Commitune.Tests.Bot;

public class EntryFormatterTests
{
    /// <summary>22:30 in São Paulo, already the next day in UTC.</summary>
    private static readonly DateTimeOffset LateNight = new(2026, 8, 19, 1, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The entry belongs to the day the user was living, not to the UTC day it fell in.
    /// Anything else files an evening's writing under tomorrow — and splits the day in two.
    /// </summary>
    [Fact]
    public void Files_a_late_night_entry_under_the_users_day()
    {
        var entry = EntryFormatter.Format(LateNight, "escrevi isso quase meia-noite");

        Assert.Equal("diario/2026/08/2026-08-18.md", entry.Path);
        Assert.Contains("## 22:30", entry.AppendedBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Pads_the_month_so_paths_sort_the_way_the_year_runs()
    {
        var entry = EntryFormatter.Format(new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero), "oi");

        Assert.Equal("diario/2026/01/2026-01-05.md", entry.Path);
    }

    [Fact]
    public void A_new_day_starts_the_file_with_the_date_as_a_heading()
    {
        var entry = EntryFormatter.Format(LateNight, "primeira do dia");

        Assert.Equal("# 18/08/2026\n\n## 22:30\n\nprimeira do dia\n", entry.NewFileContent);
    }

    [Fact]
    public void An_entry_added_to_a_day_already_started_carries_only_its_own_block()
    {
        var entry = EntryFormatter.Format(LateNight, "mais uma");

        Assert.Equal("## 22:30\n\nmais uma\n", entry.AppendedBlock);
        Assert.EndsWith(entry.AppendedBlock, entry.NewFileContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Trims_the_message_without_touching_what_is_inside_it()
    {
        var entry = EntryFormatter.Format(LateNight, "  duas linhas:\n\n  a segunda  \n\n");

        Assert.Equal("## 22:30\n\nduas linhas:\n\n  a segunda\n", entry.AppendedBlock);
    }

    [Fact]
    public void The_commit_message_says_when_the_entry_was_written()
    {
        var entry = EntryFormatter.Format(LateNight, "qualquer coisa");

        Assert.Equal("Entrada de 18/08/2026 às 22:30", entry.CommitMessage);
    }

    /// <summary>
    /// The subject line is visible in places the file is not — notification emails, the
    /// contribution graph tooltip, a shared screen. The entry stays inside the file.
    /// </summary>
    [Fact]
    public void The_commit_message_never_carries_the_entry_itself()
    {
        var entry = EntryFormatter.Format(LateNight, "hoje o resultado do exame chegou");

        Assert.DoesNotContain("exame", entry.CommitMessage, StringComparison.OrdinalIgnoreCase);
    }
}
