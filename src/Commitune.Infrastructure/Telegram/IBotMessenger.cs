namespace Commitune.Infrastructure.Telegram;

/// <summary>
/// Everything the bot can say to a user. A narrow seam over <c>ITelegramBotClient</c> so the
/// conversation logic — where the "every message gets a reply" rule lives — is testable
/// without a Bot API in the loop.
/// </summary>
public interface IBotMessenger
{
    Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <summary>Sends a message carrying a single inline button that opens <paramref name="url"/>.</summary>
    Task SendLinkAsync(
        long chatId,
        string text,
        string buttonLabel,
        Uri url,
        CancellationToken cancellationToken);
}
