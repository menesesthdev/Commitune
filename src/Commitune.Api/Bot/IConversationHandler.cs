using Commitune.Domain.Entities;

namespace Commitune.Api.Bot;

/// <summary>
/// Decides what a text message means for a user in a given onboarding state, and replies.
/// Implementations must reply on every path — see <see cref="BotReplies"/>.
/// </summary>
public interface IConversationHandler
{
    Task HandleAsync(BotUser user, string text, CancellationToken cancellationToken);
}
