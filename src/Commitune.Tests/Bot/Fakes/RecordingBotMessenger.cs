using Commitune.Infrastructure.Telegram;

namespace Commitune.Tests.Bot.Fakes;

/// <summary>Captures what the bot said, so tests can assert the user was actually answered.</summary>
public sealed class RecordingBotMessenger : IBotMessenger
{
    public List<SentMessage> Sent { get; } = [];

    /// <summary>Set to simulate Telegram itself being unreachable.</summary>
    public Exception? FailWith { get; set; }

    public SentMessage Single => Assert.Single(Sent);

    public Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken)
        => Record(new SentMessage(chatId, text, null));

    public Task SendLinkAsync(
        long chatId,
        string text,
        string buttonLabel,
        Uri url,
        CancellationToken cancellationToken)
        => Record(new SentMessage(chatId, text, url));

    private Task Record(SentMessage message)
    {
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        Sent.Add(message);

        return Task.CompletedTask;
    }

    public sealed record SentMessage(long ChatId, string Text, Uri? Link);
}
