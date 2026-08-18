using Telegram.Bot.Types;

namespace Commitune.Api.Bot;

/// <summary>
/// Entry point for everything Telegram delivers. Never throws: a failure the user does not
/// hear about is the churn risk this product cannot afford, so the router turns any error
/// into an actionable reply.
/// </summary>
public interface ITelegramUpdateRouter
{
    Task RouteAsync(Update update, CancellationToken cancellationToken);
}
