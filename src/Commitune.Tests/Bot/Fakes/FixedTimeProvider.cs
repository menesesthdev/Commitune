namespace Commitune.Tests.Bot.Fakes;

/// <summary>A clock that does not move, so an entry's date is a fact of the test.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
