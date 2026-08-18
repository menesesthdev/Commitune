using System.ComponentModel.DataAnnotations;

namespace Commitune.Infrastructure.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Token from @BotFather. Env var: <c>TELEGRAM_BOT_TOKEN</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret echoed by Telegram in the <c>X-Telegram-Bot-Api-Secret-Token</c> header.
    /// Env var: <c>WEBHOOK_SECRET_TOKEN</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string WebhookSecretToken { get; set; } = string.Empty;
}
