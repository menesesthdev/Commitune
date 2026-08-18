using Commitune.Api.Bot;
using Commitune.Infrastructure.DependencyInjection;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Commitune.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Commitune.Tests;

/// <summary>
/// A registration missed in wiring only fails at runtime, on a real user's message. Building
/// the real graph here turns that into a failing test instead.
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TELEGRAM_BOT_TOKEN"] = "123456:not-a-real-token",
                ["WEBHOOK_SECRET_TOKEN"] = "not-a-real-secret",
                ["GITHUB_CLIENT_ID"] = "client-id",
                ["GITHUB_CLIENT_SECRET"] = "client-secret",
                ["GITHUB_CALLBACK_URL"] = "https://commitune.test/oauth/github/callback",
                // Never connected to: registering a DbContext does not open a connection.
                ["POSTGRES_CONNECTION_STRING"] = "Host=localhost;Database=commitune;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCommituneInfrastructure(configuration);
        services.AddCommituneBot();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Theory]
    [InlineData(typeof(ITelegramUpdateRouter))]
    [InlineData(typeof(IConversationHandler))]
    [InlineData(typeof(IGitHubConnectionService))]
    [InlineData(typeof(IRepositoryProvisioner))]
    [InlineData(typeof(IEntryCommitter))]
    [InlineData(typeof(IAccountDisconnector))]
    [InlineData(typeof(IBotUserStore))]
    [InlineData(typeof(IBotMessenger))]
    [InlineData(typeof(IGitHubOAuthService))]
    [InlineData(typeof(IGitHubRepositoryService))]
    [InlineData(typeof(ITokenProtector))]
    [InlineData(typeof(IOAuthStateProtector))]
    public void Resolves_every_service_the_app_depends_on(Type serviceType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }
}
