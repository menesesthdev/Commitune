using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.Persistence;

namespace Commitune.Tests.Bot.Fakes;

/// <summary>In-memory stand-in for the Postgres-backed store.</summary>
public sealed class FakeBotUserStore : IBotUserStore
{
    private readonly Dictionary<long, BotUser> _users = [];

    public int SaveCount { get; private set; }

    /// <summary>Set to make the store fail, so the "user always hears about it" path can be tested.</summary>
    public Exception? FailWith { get; set; }

    public BotUser Seed(long telegramUserId, OnboardingState state, long chatId = 0)
    {
        var user = new BotUser
        {
            Id = Guid.NewGuid(),
            TelegramUserId = telegramUserId,
            TelegramChatId = chatId == 0 ? telegramUserId : chatId,
            State = state,
        };

        _users[telegramUserId] = user;

        return user;
    }

    public Task<BotUser> GetOrCreateAsync(long telegramUserId, long telegramChatId, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            return Task.FromException<BotUser>(FailWith);
        }

        if (!_users.TryGetValue(telegramUserId, out var user))
        {
            user = Seed(telegramUserId, OnboardingState.NotStarted, telegramChatId);
        }

        user.TelegramChatId = telegramChatId;

        return Task.FromResult(user);
    }

    public Task<BotUser?> FindAsync(long telegramUserId, CancellationToken cancellationToken)
        => Task.FromResult(_users.GetValueOrDefault(telegramUserId));

    public Task SaveAsync(BotUser user, CancellationToken cancellationToken)
    {
        SaveCount++;
        _users[user.TelegramUserId] = user;

        return Task.CompletedTask;
    }
}
