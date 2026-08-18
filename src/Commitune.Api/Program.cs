using Commitune.Api.Bot;
using Commitune.Api.Endpoints;
using Commitune.Infrastructure.DependencyInjection;
using Commitune.Infrastructure.Persistence;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCommituneInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IConversationHandler, ConversationHandler>();
builder.Services.AddScoped<ITelegramUpdateRouter, TelegramUpdateRouter>();

// Telegram's Update graph relies on the library's own polymorphic converters; without this
// the webhook body binds to a half-empty object instead of failing loudly.
builder.Services.ConfigureHttpJsonOptions(options => JsonBotAPI.Configure(options.SerializerOptions));

var app = builder.Build();

await app.MigrateCommituneDatabaseAsync();

app.UseExceptionHandler();

app.MapHealthEndpoints();
app.MapTelegramWebhookEndpoints();
app.MapGitHubOAuthEndpoints();

app.Run();

/// <summary>Exposed so integration tests can spin the app up with WebApplicationFactory.</summary>
public partial class Program;
