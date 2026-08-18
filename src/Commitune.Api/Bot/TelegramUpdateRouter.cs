using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Telegram;
using Telegram.Bot.Types;

namespace Commitune.Api.Bot;

public sealed class TelegramUpdateRouter(
    IBotUserStore users,
    IBotMessenger messenger,
    IConversationHandler conversation,
    ILogger<TelegramUpdateRouter> logger) : ITelegramUpdateRouter
{
    public async Task RouteAsync(Update update, CancellationToken cancellationToken)
    {
        var message = update.Message;

        // Edits, callback queries, channel posts and messages from other bots are not part of
        // the conversation. Nobody is waiting on a reply for those, so dropping them is silent
        // by design — unlike dropping a real message, which never is.
        if (message?.From is not { IsBot: false } sender)
        {
            return;
        }

        var chatId = message.Chat.Id;

        try
        {
            var user = await users.GetOrCreateAsync(sender.Id, chatId, cancellationToken);

            if (message.Text is not { Length: > 0 } text)
            {
                await messenger.SendTextAsync(chatId, BotReplies.UnsupportedMessage, cancellationToken);
                return;
            }

            await conversation.HandleAsync(user, text, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Safe to log: ids and the message length, never the token and never the content.
            logger.LogError(
                exception,
                "Failed to handle an update from Telegram user {TelegramUserId}.",
                sender.Id);

            await TryApologizeAsync(chatId, cancellationToken);
        }
    }

    /// <summary>
    /// Last resort. If Telegram itself is what failed, there is nowhere left to report to —
    /// swallow it so the webhook still answers 200 instead of asking for a redelivery that
    /// would fail the same way.
    /// </summary>
    private async Task TryApologizeAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            await messenger.SendTextAsync(chatId, BotReplies.SomethingWentWrong, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not deliver the failure notice to chat {ChatId}.", chatId);
        }
    }
}
