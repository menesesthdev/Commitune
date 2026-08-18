namespace Commitune.Infrastructure.GitHub;

/// <summary>Identifies the repository a user's entries are committed to.</summary>
public readonly record struct RepositoryReference(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";
}
