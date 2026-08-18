using Commitune.Api.Bot;
using Commitune.Domain.Entities;
using Commitune.Tests.Bot.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types;

namespace Commitune.Tests.Bot;

public class TelegramUpdateRouterTests
{
    private const long TelegramUserId = 777;
    private const long ChatId = 555;

    private readonly FakeBotUserStore _users = new();
    private readonly RecordingBotMessenger _messenger = new();
    private readonly SpyConversationHandler _conversation = new();

    private TelegramUpdateRouter CreateRouter()
        => new(_users, _messenger, _conversation, NullLogger<TelegramUpdateRouter>.Instance);

    private static Update TextUpdate(string text, bool fromBot = false) => new()
    {
        Message = new Message
        {
            Text = text,
            Chat = new Chat { Id = ChatId },
            From = new User { Id = TelegramUserId, IsBot = fromBot, FirstName = "Tester" },
        },
    };

    [Fact]
    public async Task Hands_a_text_message_to_the_conversation()
    {
        await CreateRouter().RouteAsync(TextUpdate("/start"), CancellationToken.None);

        Assert.Equal("/start", _conversation.LastText);
        Assert.Equal(TelegramUserId, _conversation.LastUser!.TelegramUserId);
        Assert.Equal(ChatId, _conversation.LastUser.TelegramChatId);
    }

    [Fact]
    public async Task Ignores_an_update_that_carries_no_message()
    {
        await CreateRouter().RouteAsync(new Update(), CancellationToken.None);

        Assert.Empty(_messenger.Sent);
        Assert.Null(_conversation.LastText);
    }

    [Fact]
    public async Task Ignores_a_message_sent_by_another_bot()
    {
        await CreateRouter().RouteAsync(TextUpdate("/start", fromBot: true), CancellationToken.None);

        Assert.Empty(_messenger.Sent);
        Assert.Null(_conversation.LastText);
    }

    [Fact]
    public async Task Answers_a_message_that_is_not_text_instead_of_dropping_it()
    {
        var update = TextUpdate("x");
        update.Message!.Text = null;

        await CreateRouter().RouteAsync(update, CancellationToken.None);

        Assert.Equal(BotReplies.UnsupportedMessage, _messenger.Single.Text);
        Assert.Null(_conversation.LastText);
    }

    /// <summary>
    /// The rule that matters most: a message accepted and then dropped, with the user hearing
    /// nothing, is the failure mode this product cannot have.
    /// </summary>
    [Fact]
    public async Task Tells_the_user_when_handling_blows_up()
    {
        _conversation.FailWith = new InvalidOperationException("GitHub is down");

        await CreateRouter().RouteAsync(TextUpdate("uma anotação"), CancellationToken.None);

        var sent = _messenger.Single;
        Assert.Equal(ChatId, sent.ChatId);
        Assert.Equal(BotReplies.SomethingWentWrong, sent.Text);
    }

    [Fact]
    public async Task Tells_the_user_when_the_database_is_unreachable()
    {
        _users.FailWith = new InvalidOperationException("connection refused");

        await CreateRouter().RouteAsync(TextUpdate("/start"), CancellationToken.None);

        Assert.Equal(BotReplies.SomethingWentWrong, _messenger.Single.Text);
    }

    [Fact]
    public async Task Does_not_throw_when_telegram_itself_is_unreachable()
    {
        _conversation.FailWith = new InvalidOperationException("boom");
        _messenger.FailWith = new HttpRequestException("telegram unreachable");

        // A throw here would turn into a non-2xx and make Telegram redeliver an update that
        // would fail exactly the same way.
        await CreateRouter().RouteAsync(TextUpdate("oi"), CancellationToken.None);
    }

    [Fact]
    public async Task Lets_cancellation_propagate_instead_of_apologizing_for_it()
    {
        _conversation.FailWith = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateRouter().RouteAsync(TextUpdate("oi"), CancellationToken.None));

        Assert.Empty(_messenger.Sent);
    }

    private sealed class SpyConversationHandler : IConversationHandler
    {
        public BotUser? LastUser { get; private set; }

        public string? LastText { get; private set; }

        public Exception? FailWith { get; set; }

        public Task HandleAsync(BotUser user, string text, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                return Task.FromException(FailWith);
            }

            LastUser = user;
            LastText = text;

            return Task.CompletedTask;
        }
    }
}
