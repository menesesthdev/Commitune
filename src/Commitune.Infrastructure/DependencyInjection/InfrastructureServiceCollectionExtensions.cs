using Commitune.Infrastructure.Configuration;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Commitune.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
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

        services.AddSingleton<ITelegramBotClient>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TelegramOptions>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");
            return new TelegramBotClient(options.BotToken, httpClient);
        });
    }
}
