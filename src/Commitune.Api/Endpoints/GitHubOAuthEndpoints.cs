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

    private static Task<IResult> HandleCallbackAsync(
        string? code,
        string? state,
        string? error,
        IOAuthStateProtector stateProtector,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
        {
            // The user declined on GitHub's consent screen.
            return Task.FromResult(Results.Text("Authorization was cancelled. Go back to Telegram and try again."));
        }

        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(Results.BadRequest("Missing authorization code."));
        }

        // The state carries the telegram_user_id; an unverified one is an account-takeover vector.
        if (!stateProtector.TryValidate(state, out var telegramUserId))
        {
            return Task.FromResult(Results.BadRequest("This authorization link is invalid or has expired."));
        }

        // Safe to log: the Telegram user id is an identifier, not a credential.
        loggerFactory.CreateLogger(typeof(GitHubOAuthEndpoints))
            .LogInformation("Verified GitHub callback for Telegram user {TelegramUserId}.", telegramUserId);

        // TODO: exchange the code, protect the token, move the user to AwaitingRepoName and
        // ask them — over Telegram — what the repository should be called.

        return Task.FromResult(Results.Text("GitHub connected. Head back to Telegram to finish setup."));
    }
}
