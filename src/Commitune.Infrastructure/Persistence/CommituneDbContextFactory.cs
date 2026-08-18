using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Commitune.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Scaffolding a migration needs the model, not a reachable
/// database, so it must not depend on the app's configured secrets — otherwise adding a
/// migration would require a full <c>.env</c>.
/// </summary>
public sealed class CommituneDbContextFactory : IDesignTimeDbContextFactory<CommituneDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=commitune;Username=commitune;Password=design-time";

    public CommituneDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<CommituneDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CommituneDbContext(options);
    }
}
