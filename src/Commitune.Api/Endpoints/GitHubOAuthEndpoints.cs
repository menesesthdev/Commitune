using Commitune.Api.Bot;
using Commitune.Infrastructure.Security;

namespace Commitune.Api.Endpoints;

public static class GitHubOAuthEndpoints
{
    public const string CallbackRoute = "/oauth/github/callback";

    public static IEndpointRouteBuilder MapGitHubOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(CallbackRoute, HandleCallbackAsync)
            .WithName("GitHubOAuthCallback");

        return app;
    }

    private static async Task<IResult> HandleCallbackAsync(
        string? code,
        string? state,
        string? error,
        IOAuthStateProtector stateProtector,
        IGitHubConnectionService connectionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
        {
            // The user declined on GitHub's consent screen.
            return CallbackPage.Neutral(
                "Autorização cancelada",
                "Você cancelou a autorização. Volte ao Telegram e mande /start quando quiser tentar de novo.");
        }

        if (string.IsNullOrEmpty(code))
        {
            return CallbackPage.Neutral("Link incompleto", "Faltou o código de autorização nesta URL.");
        }

        // The state carries the telegram_user_id; an unverified one is an account-takeover vector.
        if (!stateProtector.TryValidate(state, out var telegramUserId))
        {
            return CallbackPage.Neutral(
                "Link inválido ou expirado",
                "Este link de autorização não vale mais. Volte ao Telegram e mande /start para gerar outro.");
        }

        // Safe to log: the Telegram user id is an identifier, not a credential.
        loggerFactory.CreateLogger(typeof(GitHubOAuthEndpoints))
            .LogInformation("Verified GitHub callback for Telegram user {TelegramUserId}.", telegramUserId);

        var outcome = await connectionService.CompleteAsync(telegramUserId, code, cancellationToken);

        return outcome switch
        {
            GitHubConnectionOutcome.AwaitingRepoName => CallbackPage.Success(
                "GitHub conectado!",
                "Volte ao Telegram — só falta escolher o nome do seu repositório."),

            GitHubConnectionOutcome.Reconnected => CallbackPage.Success(
                "Reconectado!",
                "Sua autorização foi renovada. Pode voltar ao Telegram e seguir escrevendo."),

            GitHubConnectionOutcome.UnknownUser => CallbackPage.Neutral(
                "Não encontrei sua conversa",
                "Volte ao Telegram e mande /start para começar de novo."),

            _ => CallbackPage.Neutral(
                "Não deu certo",
                "O GitHub recusou a autorização. Volte ao Telegram e mande /start para tentar de novo."),
        };
    }
}

/// <summary>
/// The only HTML the API serves. It exists because GitHub redirects a real browser here and
/// the user needs somewhere to land — the product itself stays inside the Telegram chat, so
/// this page says one thing and sends them back.
/// </summary>
internal static class CallbackPage
{
    public static IResult Success(string title, string message) => Render(title, message, "#2ea043");

    public static IResult Neutral(string title, string message) => Render(title, message, "#57606a");

    private static IResult Render(string title, string message, string accent)
    {
        // $$ so the CSS braces stay literal and only {{...}} interpolates.
        var html = $$"""
            <!doctype html>
            <html lang="pt-br">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{title}} — Commitune</title>
              <style>
                :root { color-scheme: light dark; }
                body {
                  margin: 0; min-height: 100vh; display: grid; place-items: center;
                  font: 16px/1.6 system-ui, sans-serif; padding: 24px;
                }
                main { max-width: 34rem; text-align: center; }
                h1 { font-size: 1.5rem; margin: 0 0 .5rem; color: {{accent}}; }
                p { margin: 0; opacity: .8; }
              </style>
            </head>
            <body>
              <main>
                <h1>{{title}}</h1>
                <p>{{message}}</p>
              </main>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8");
    }
}
