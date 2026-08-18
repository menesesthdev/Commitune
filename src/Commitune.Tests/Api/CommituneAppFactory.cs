using Commitune.Domain.Entities;
using Commitune.Domain.Onboarding;
using Commitune.Infrastructure.GitHub;
using Commitune.Infrastructure.Persistence;
using Commitune.Infrastructure.Security;
using Commitune.Infrastructure.Telegram;
using Commitune.Tests.Bot.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Octokit has a RepositoryReference of its own; ours is the one meant here.
using RepositoryReference = Commitune.Infrastructure.GitHub.RepositoryReference;

namespace Commitune.Tests.Api;

/// <summary>
/// Boots the real application — real DI graph, real endpoints, real routing, real JSON
/// configuration — with only the two edges the process cannot own replaced: Telegram out and
/// GitHub out. Everything between the HTTP request and those edges is the code that ships.
///
/// This is the layer the unit tests cannot reach: whether Telegram's own JSON binds to
/// <c>Update</c>, whether the secret-token check runs before anything else, whether the graph
/// even resolves under a real request.
/// </summary>
public sealed class CommituneAppFactory : WebApplicationFactory<Program>
{
    /// <summary>One store per factory, so tests running in parallel do not share users.</summary>
    private readonly string _databaseName = $"commitune-{Guid.NewGuid()}";

    public RecordingBotMessenger Messenger { get; } = new();

    public FakeGitHubOAuthService GitHubOAuth { get; } = new();

    public FakeGitHubRepositoryService GitHubRepositories { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // The options are [Required] and validated on start: a missing one fails the boot,
        // which is the behaviour we want in production and have to satisfy here.
        builder.UseSetting("TELEGRAM_BOT_TOKEN", "123456:not-a-real-token");
        builder.UseSetting("WEBHOOK_SECRET_TOKEN", WebhookSecret);
        builder.UseSetting("GITHUB_CLIENT_ID", "client-id");
        builder.UseSetting("GITHUB_CLIENT_SECRET", "client-secret");
        builder.UseSetting("GITHUB_CALLBACK_URL", "https://commitune.test/oauth/github/callback");

        // Required to build the graph; never connected to — the provider is replaced below.
        builder.UseSetting("POSTGRES_CONNECTION_STRING", "Host=localhost;Database=commitune;Username=u;Password=p");

        builder.ConfigureServices(services =>
        {
            ReplaceDatabaseWithMemory(services, _databaseName);

            services.RemoveAll<IBotMessenger>();
            services.AddSingleton<IBotMessenger>(Messenger);

            services.RemoveAll<IGitHubOAuthService>();
            services.AddSingleton<IGitHubOAuthService>(GitHubOAuth);

            services.RemoveAll<IGitHubRepositoryService>();
            services.AddSingleton<IGitHubRepositoryService>(GitHubRepositories);
        });
    }

    public const string WebhookSecret = "webhook-secret-for-tests";

    /// <summary>
    /// Drops every registration Npgsql's <c>AddDbContext</c> left behind before adding the
    /// in-memory one. Removing only <c>DbContextOptions</c> leaves the provider configuration
    /// in place, and EF refuses to boot with two providers registered.
    /// </summary>
    private static void ReplaceDatabaseWithMemory(IServiceCollection services, string databaseName)
    {
        var registrations = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(CommituneDbContext)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericArguments().Contains(typeof(CommituneDbContext))))
            .ToList();

        foreach (var registration in registrations)
        {
            services.Remove(registration);
        }

        services.AddDbContext<CommituneDbContext>(options => options.UseInMemoryDatabase(databaseName));
    }

    /// <summary>Reads the user the way the app left it, through the app's own context.</summary>
    public async Task<BotUser?> FindUserAsync(long telegramUserId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommituneDbContext>();

        return await dbContext.Users.SingleOrDefaultAsync(u => u.TelegramUserId == telegramUserId);
    }

    /// <summary>
    /// Puts a user in the database in the state a test needs, with the token protected by the
    /// running app's own key ring — the same path the OAuth callback would have written.
    /// </summary>
    public async Task<BotUser> SeedUserAsync(
        long telegramUserId,
        OnboardingState state,
        string? accessToken = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommituneDbContext>();
        var tokenProtector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();

        var onboarded = state is OnboardingState.Ready or OnboardingState.Paused;

        var user = new BotUser
        {
            TelegramUserId = telegramUserId,
            TelegramChatId = telegramUserId,
            State = state,
            GithubLogin = accessToken is null ? null : "tester",
            ProtectedGithubToken = accessToken is null ? null : tokenProtector.Protect(accessToken),
            RepositoryOwner = onboarded ? "tester" : null,
            RepositoryName = onboarded ? "til" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    public Task<BotUser> SeedReadyUserAsync(long telegramUserId, string accessToken = "gho_fake")
        => SeedUserAsync(telegramUserId, OnboardingState.Ready, accessToken);

    /// <summary>Signs a state the way <c>/start</c> would, for the callback tests.</summary>
    public string CreateOAuthState(long telegramUserId)
    {
        using var scope = Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IOAuthStateProtector>().Create(telegramUserId);
    }
}

public sealed class FakeGitHubOAuthService : IGitHubOAuthService
{
    public GitHubAuthorization Authorization { get; set; } = new("gho_fromTheExchange", "tester");

    public string? ExchangedCode { get; private set; }

    public Uri BuildAuthorizationUrl(string state)
        => new($"https://github.com/login/oauth/authorize?client_id=test&state={Uri.EscapeDataString(state)}");

    public Task<GitHubAuthorization> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        ExchangedCode = code;

        return Task.FromResult(Authorization);
    }

    public Task RevokeAsync(string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class FakeGitHubRepositoryService : IGitHubRepositoryService
{
    public static readonly Uri EntryUrl =
        new("https://github.com/tester/til/blob/main/til/2026-08-18-indices-parciais.md");

    public string? UsedAccessToken { get; private set; }

    public TilEntry WrittenEntry { get; private set; }

    public Task<RepositoryReference> CreatePrivateRepositoryAsync(
        string accessToken,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        UsedAccessToken = accessToken;

        return Task.FromResult(new RepositoryReference("tester", repositoryName));
    }

    public Task<CommittedEntry> CommitEntryAsync(
        string accessToken,
        RepositoryReference repository,
        TilEntry entry,
        CancellationToken cancellationToken)
    {
        UsedAccessToken = accessToken;
        WrittenEntry = entry;

        return Task.FromResult(new CommittedEntry("c0ffee", $"{entry.PathPrefix}.md", EntryUrl));
    }
}
