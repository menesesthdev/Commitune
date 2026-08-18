namespace Commitune.Infrastructure.GitHub;

/// <summary>Identifies the repository a user's entries are committed to.</summary>
public readonly record struct RepositoryReference(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";
}

/// <summary>
/// A repository that already exists on GitHub. <paramref name="IsPrivate"/> is the whole point
/// of this type: Commitune writes entries to private repositories only, and a repository it did
/// not create is the one case where that is not guaranteed by construction.
/// </summary>
public readonly record struct ExistingRepository(RepositoryReference Reference, bool IsPrivate);
