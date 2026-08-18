namespace Commitune.Api.Bot;

/// <summary>
/// The half of onboarding that happens outside Telegram: turning the OAuth callback into a
/// stored authorization, then pulling the user back into the chat to name their repository.
/// </summary>
public interface IGitHubConnectionService
{
    Task<GitHubConnectionOutcome> CompleteAsync(
        long telegramUserId,
        string code,
        CancellationToken cancellationToken);
}

public enum GitHubConnectionOutcome
{
    /// <summary>Authorized; the user was asked, over Telegram, to name the repository.</summary>
    AwaitingRepoName,

    /// <summary>A user who already finished onboarding reauthorized — the token was refreshed.</summary>
    Reconnected,

    /// <summary>The signed state was valid but no such user exists. Should not happen.</summary>
    UnknownUser,

    /// <summary>GitHub refused the code exchange.</summary>
    Failed,
}
