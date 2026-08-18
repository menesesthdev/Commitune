using Commitune.Infrastructure.Configuration;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Commitune.Infrastructure.Telegram;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Commitune.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Named client backing <see cref="ITelegramBotClient"/>. See the note on its logging.</summary>
    public const string TelegramHttpClientName = "telegram";

    public static IServiceCollection AddCommituneInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCommituneOptions(configuration);
        services.AddCommitunePersistence(configuration);
        services.AddCommituneDataProtection(configuration);
        services.AddCommituneClients();

        return services;
    }

    private static void AddCommituneOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // README documents flat env var names (TELEGRAM_BOT_TOKEN, …), so bind the config
        // section first and let the documented variable win when it is present.
        services.AddOptions<TelegramOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection(TelegramOptions.SectionName).Bind(options);
                options.BotToken = config["TELEGRAM_BOT_TOKEN"] ?? options.BotToken;
                options.WebhookSecretToken = config["WEBHOOK_SECRET_TOKEN"] ?? options.WebhookSecretToken;
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GitHubOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection(GitHubOptions.SectionName).Bind(options);
                options.ClientId = config["GITHUB_CLIENT_ID"] ?? options.ClientId;
                options.ClientSecret = config["GITHUB_CLIENT_SECRET"] ?? options.ClientSecret;
                options.CallbackUrl = config["GITHUB_CALLBACK_URL"] ?? options.CallbackUrl;
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddCommitunePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["POSTGRES_CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "No Postgres connection string. Set POSTGRES_CONNECTION_STRING or ConnectionStrings:Postgres.");

        services.AddDbContext<CommituneDbContext>(options => options.UseNpgsql(connectionString));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IBotUserStore, BotUserStore>();
    }

    private static void AddCommituneDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddDataProtection()
            // Keys must survive a redeploy, otherwise every stored token becomes undecryptable.
            .SetApplicationName("Commitune");

        var keyPath = configuration["DATA_PROTECTION_KEY_PATH"];
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            builder.PersistKeysToFileSystem(Directory.CreateDirectory(keyPath));
        }

        services.AddSingleton<ITokenProtector, DataProtectionTokenProtector>();
        services.AddSingleton<IOAuthStateProtector, DataProtectionOAuthStateProtector>();
    }

    private static void AddCommituneClients(this IServiceCollection services)
    {
        services.AddHttpClient<IGitHubOAuthService, GitHubOAuthService>(client =>
        {
            client.DefaultRequestHeaders.Accept.Add(new("application/json"));
            client.DefaultRequestHeaders.UserAgent.Add(new("Commitune", "1.0"));
        });

        services.AddScoped<IGitHubRepositoryService, GitHubRepositoryService>();

        services.AddHttpClient(TelegramHttpClientName, client =>
            {
                // The webhook request is held open while the reply is sent, and Telegram gives
                // up on us long before HttpClient's 100s default would.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            // Every Bot API URL is https://api.telegram.org/bot<TOKEN>/..., and the default
            // IHttpClientFactory logging writes that URL at Information level — which puts the
            // bot token in the logs on every single send. Nothing about this client may be logged.
            .RemoveAllLoggers();

        services.AddSingleton<ITelegramBotClient>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(TelegramHttpClientName);
            return new TelegramBotClient(options.BotToken, httpClient);
        });

        services.AddSingleton<IBotMessenger, TelegramBotMessenger>();
    }
}
