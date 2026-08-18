using Commitune.Api.Bot;

namespace Commitune.Tests.Bot;

public class RepositoryNameRulesTests
{
    [Theory]
    [InlineData("diario")]
    [InlineData("meu-diario")]
    [InlineData("meu_diario")]
    [InlineData("diario.2026")]
    [InlineData("A1")]
    public void Accepts_a_name_github_accepts(string name)
        => Assert.True(RepositoryNameRules.IsValid(name));

    [Theory]
    [InlineData("")]
    [InlineData("meu diario")]
    [InlineData("diário")]
    [InlineData("diario!")]
    [InlineData("meu/diario")]
    [InlineData(".")]
    [InlineData("..")]
    public void Rejects_a_name_github_would_reject_or_rewrite(string name)
        => Assert.False(RepositoryNameRules.IsValid(name));

    [Fact]
    public void Rejects_a_name_over_a_hundred_characters()
        => Assert.False(RepositoryNameRules.IsValid(new string('a', RepositoryNameRules.MaxLength + 1)));

    [Theory]
    [InlineData("meu diario", "meu-diario")]
    [InlineData("Meu Diário", "meu-diario")]
    [InlineData("  anotações  ", "anotacoes")]
    [InlineData("diário de bordo!", "diario-de-bordo")]
    [InlineData("já   é", "ja-e")]
    public void Suggests_the_closest_legal_name(string input, string expected)
        => Assert.Equal(expected, RepositoryNameRules.Suggest(input));

    [Fact]
    public void Suggests_nothing_when_there_is_nothing_left_to_suggest()
    {
        Assert.Null(RepositoryNameRules.Suggest("???"));
        Assert.Null(RepositoryNameRules.Suggest("   "));
        Assert.Null(RepositoryNameRules.Suggest(null));
    }

    [Fact]
    public void Never_suggests_a_name_it_would_reject()
    {
        foreach (var input in new[] { "meu diario", "Diário Pessoal!!", new string('x', 200), "a b c" })
        {
            if (RepositoryNameRules.Suggest(input) is { } suggestion)
            {
                Assert.True(RepositoryNameRules.IsValid(suggestion), $"suggested an invalid name for '{input}'");
            }
        }
    }
}
