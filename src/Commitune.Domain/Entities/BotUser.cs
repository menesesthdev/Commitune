using Commitune.Domain.Onboarding;

namespace Commitune.Domain.Entities;

/// <summary>
/// A Telegram user connected to Commitune. One row per <see cref="TelegramUserId"/>.
/// </summary>
public class BotUser
{
    public Guid Id { get; set; }

    /// <summary>Telegram's user id — the tenant key for everything in this system.</summary>
    public long TelegramUserId { get; set; }

    /// <summary>Chat to reply into. For private chats this equals <see cref="TelegramUserId"/>.</summary>
    public long TelegramChatId { get; set; }

    public OnboardingState State { get; set; } = OnboardingState.NotStarted;

    public string? GithubLogin { get; set; }

    /// <summary>
    /// The GitHub access token, encrypted with the Data Protection API. Never assign a
    /// plaintext token here and never log this value, not even truncated.
    /// </summary>
    public string? ProtectedGithubToken { get; set; }

    public string? RepositoryOwner { get; set; }

    public string? RepositoryName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
