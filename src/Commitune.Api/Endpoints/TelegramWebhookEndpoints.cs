using System.Security.Cryptography;
using System.Text;
using Commitune.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace Commitune.Api.Endpoints;

public static class TelegramWebhookEndpoints
{
    public const string Route = "/telegram/webhook";

    public const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static IEndpointRouteBuilder MapTelegramWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, HandleUpdateAsync)
            .WithName("TelegramWebhook")
            .ExcludeFromDescription();

        return app;
    }

    private static Task<IResult> HandleUpdateAsync(
        HttpContext httpContext,
        Update update,
        IOptions<TelegramOptions> telegramOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsFromTelegram(httpContext.Request, telegramOptions.Value.WebhookSecretToken))
        {
            var logger = loggerFactory.CreateLogger(typeof(TelegramWebhookEndpoints));
            logger.LogWarning("Rejected a webhook call with a missing or invalid secret token.");
            return Task.FromResult(Results.Unauthorized());
        }

        // TODO: dispatch the update to the onboarding state machine / commit pipeline.
        // Whatever it decides, the user must always get a reply — silent failure is a bug.

        // Always 200 once authenticated: a non-2xx makes Telegram redeliver the same update.
        return Task.FromResult(Results.Ok());
    }

    /// <summary>
    /// Compares the header Telegram echoes back against the configured secret. An unset
    /// secret rejects everything rather than letting the endpoint run unauthenticated.
    /// </summary>
    private static bool IsFromTelegram(HttpRequest request, string expectedSecret)
    {
        if (string.IsNullOrEmpty(expectedSecret))
        {
            return false;
        }

        if (!request.Headers.TryGetValue(SecretTokenHeader, out var provided) || provided.Count != 1)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided[0] ?? string.Empty),
            Encoding.UTF8.GetBytes(expectedSecret));
    }
}
