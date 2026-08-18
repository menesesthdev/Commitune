using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Commitune.Infrastructure.Persistence;

public static class PersistenceStartupExtensions
{
    /// <summary>
    /// Applies pending migrations at startup. Deliberate for this deployment: one API
    /// container on one instance, so there is no second replica to race with. If the app ever
    /// scales out, this moves to a separate step before the containers come up.
    /// </summary>
    public static async Task MigrateCommituneDatabaseAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(PersistenceStartupExtensions));

        var dbContext = scope.ServiceProvider.GetRequiredService<CommituneDbContext>();

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}.", pending.Length, pending);

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
