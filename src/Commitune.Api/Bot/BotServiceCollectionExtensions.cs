namespace Commitune.Api.Bot;

public static class BotServiceCollectionExtensions
{
    /// <summary>
    /// The conversation side of the app. Kept out of <c>Program.cs</c> so the whole graph can
    /// be built and validated in a test — a missing registration only shows up at runtime,
    /// on a real user's message.
    /// </summary>
    public static IServiceCollection AddCommituneBot(this IServiceCollection services)
    {
        services.AddScoped<ITelegramUpdateRouter, TelegramUpdateRouter>();
        services.AddScoped<IConversationHandler, ConversationHandler>();
        services.AddScoped<IRepositoryProvisioner, RepositoryProvisioner>();
        services.AddScoped<IEntryCommitter, EntryCommitter>();
        services.AddScoped<IGitHubConnectionService, GitHubConnectionService>();

        return services;
    }
}
