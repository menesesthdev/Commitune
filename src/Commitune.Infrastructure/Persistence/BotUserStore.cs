using Commitune.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Commitune.Infrastructure.Persistence;

public sealed class BotUserStore(CommituneDbContext dbContext, TimeProvider timeProvider) : IBotUserStore
{
    public async Task<BotUser> GetOrCreateAsync(
        long telegramUserId,
        long telegramChatId,
        CancellationToken cancellationToken)
    {
        var user = await FindAsync(telegramUserId, cancellationToken);

        if (user is not null)
        {
            // Telegram can hand us a different chat id than the one stored (a user who
            // deleted the chat and started over); replies must follow the current one.
            if (user.TelegramChatId != telegramChatId)
            {
                user.TelegramChatId = telegramChatId;
                await SaveAsync(user, cancellationToken);
            }

            return user;
        }

        var now = timeProvider.GetUtcNow();

        user = new BotUser
        {
            TelegramUserId = telegramUserId,
            TelegramChatId = telegramChatId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two updates from the same brand-new user can race into the unique index on
            // telegram_user_id. The row the other one wrote is just as good as ours.
            dbContext.Entry(user).State = EntityState.Detached;

            var winner = await FindAsync(telegramUserId, cancellationToken);
            if (winner is null)
            {
                // Not the race, then — a real failure worth surfacing.
                throw;
            }

            return winner;
        }

        return user;
    }

    public Task<BotUser?> FindAsync(long telegramUserId, CancellationToken cancellationToken)
        => dbContext.Users.SingleOrDefaultAsync(u => u.TelegramUserId == telegramUserId, cancellationToken);

    public Task SaveAsync(BotUser user, CancellationToken cancellationToken)
    {
        user.UpdatedAt = timeProvider.GetUtcNow();

        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
