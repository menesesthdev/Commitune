using Commitune.Domain.Entities;

namespace Commitune.Infrastructure.Persistence;

/// <summary>
/// The only way the bot reads and writes users. Deliberately not a generic repository:
/// the domain is one entity keyed by <c>telegram_user_id</c>, and that is the whole surface.
/// </summary>
public interface IBotUserStore
{
    /// <summary>
    /// Returns the user for this Telegram id, creating one in
    /// <see cref="Domain.Onboarding.OnboardingState.NotStarted"/> if it is the first contact.
    /// Also refreshes the chat id, since that is where every reply is delivered.
    /// </summary>
    Task<BotUser> GetOrCreateAsync(long telegramUserId, long telegramChatId, CancellationToken cancellationToken);

    /// <summary>Looks the user up without creating one. Used by the OAuth callback.</summary>
    Task<BotUser?> FindAsync(long telegramUserId, CancellationToken cancellationToken);

    /// <summary>Persists changes made to a tracked user and stamps <c>UpdatedAt</c>.</summary>
    Task SaveAsync(BotUser user, CancellationToken cancellationToken);
}
