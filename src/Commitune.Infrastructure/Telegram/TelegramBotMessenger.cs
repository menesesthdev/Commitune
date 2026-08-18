using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Commitune.Infrastructure.Telegram;

public sealed class TelegramBotMessenger(ITelegramBotClient botClient) : IBotMessenger
{
    public Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken)
        => botClient.SendMessage(
            chatId,
            text,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);

    public Task SendLinkAsync(
        long chatId,
        string text,
        string buttonLabel,
        Uri url,
        CancellationToken cancellationToken)
        => botClient.SendMessage(
            chatId,
            text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl(buttonLabel, url.ToString())),
            cancellationToken: cancellationToken);
}
