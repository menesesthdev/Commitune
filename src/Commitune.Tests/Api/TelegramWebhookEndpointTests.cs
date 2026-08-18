using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Commitune.Api.Endpoints;
using Commitune.Domain.Onboarding;

namespace Commitune.Tests.Api;

/// <summary>
/// The webhook, exercised over real HTTP with the payloads Telegram actually posts. Every unit
/// test in this suite starts from an <c>Update</c> object that someone already built by hand —
/// this is the only place that proves Telegram's JSON becomes one.
/// </summary>
public class TelegramWebhookEndpointTests : IDisposable
{
    private const long TelegramUserId = 4242;

    private readonly CommituneAppFactory _app = new();

    public void Dispose()
    {
        _app.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A private-chat <c>/start</c>, in the shape the Bot API documents.</summary>
    private const string StartUpdateJson = """
        {
          "update_id": 900001,
          "message": {
            "message_id": 11,
            "from": {"id": 4242, "is_bot": false, "first_name": "Nicholas", "language_code": "pt-br"},
            "chat": {"id": 4242, "first_name": "Nicholas", "type": "private"},
            "date": 1786000000,
            "text": "/start",
            "entities": [{"offset": 0, "length": 6, "type": "bot_command"}]
          }
        }
        """;

    private static string MessageUpdateJson(string text) => $$"""
        {
          "update_id": 900002,
          "message": {
            "message_id": 12,
            "from": {"id": 4242, "is_bot": false, "first_name": "Nicholas"},
            "chat": {"id": 4242, "first_name": "Nicholas", "type": "private"},
            "date": 1786000060,
            "text": {{System.Text.Json.JsonSerializer.Serialize(text)}}
          }
        }
        """;

    private Task<HttpResponseMessage> PostAsync(string json, string? secret = CommituneAppFactory.WebhookSecret)
    {
        var client = _app.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, TelegramWebhookEndpoints.Route)
        {
            Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };

        if (secret is not null)
        {
            request.Headers.Add(TelegramWebhookEndpoints.SecretTokenHeader, secret);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task A_start_from_a_new_user_is_accepted_and_answered()
    {
        var response = await PostAsync(StartUpdateJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sent = Assert.Single(_app.Messenger.Sent);
        Assert.Equal(TelegramUserId, sent.ChatId);
        Assert.StartsWith("https://github.com/login/oauth/authorize", sent.Link?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_start_from_a_new_user_is_persisted_mid_onboarding()
    {
        await PostAsync(StartUpdateJson);

        var user = await _app.FindUserAsync(TelegramUserId);

        Assert.NotNull(user);
        Assert.Equal(OnboardingState.AwaitingGithubAuth, user.State);
        Assert.Equal(TelegramUserId, user.TelegramChatId);
    }

    /// <summary>
    /// The rule from CLAUDE.md: anything without the secret token is not Telegram. It has to be
    /// refused before the update is looked at, not after.
    /// </summary>
    [Fact]
    public async Task A_call_without_the_secret_token_is_refused()
    {
        var response = await PostAsync(StartUpdateJson, secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_app.Messenger.Sent);
        Assert.Null(await _app.FindUserAsync(TelegramUserId));
    }

    [Fact]
    public async Task A_call_with_the_wrong_secret_token_is_refused()
    {
        var response = await PostAsync(StartUpdateJson, secret: "quase-o-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_app.Messenger.Sent);
        Assert.Null(await _app.FindUserAsync(TelegramUserId));
    }

    /// <summary>
    /// Telegram redelivers anything it does not get a 2xx for. An update we choose to ignore is
    /// still an update we accepted — answering 500 would have it sent again forever.
    /// </summary>
    [Fact]
    public async Task An_update_that_is_not_a_message_is_accepted_and_ignored()
    {
        const string editedJson = """
            {
              "update_id": 900003,
              "edited_message": {
                "message_id": 13,
                "from": {"id": 4242, "is_bot": false, "first_name": "Nicholas"},
                "chat": {"id": 4242, "first_name": "Nicholas", "type": "private"},
                "date": 1786000120,
                "edit_date": 1786000180,
                "text": "corrigido"
              }
            }
            """;

        var response = await PostAsync(editedJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_app.Messenger.Sent);
    }

    /// <summary>A message with no text at all — a photo — must not go silent either.</summary>
    [Fact]
    public async Task A_message_without_text_gets_an_answer_anyway()
    {
        const string photoJson = """
            {
              "update_id": 900004,
              "message": {
                "message_id": 14,
                "from": {"id": 4242, "is_bot": false, "first_name": "Nicholas"},
                "chat": {"id": 4242, "first_name": "Nicholas", "type": "private"},
                "date": 1786000240,
                "photo": [{"file_id": "abc", "file_unique_id": "def", "width": 90, "height": 90, "file_size": 1234}]
              }
            }
            """;

        var response = await PostAsync(photoJson);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_app.Messenger.Sent);
    }

    /// <summary>
    /// The whole product in one request: Telegram's JSON in, a TIL committed, a reply out.
    /// </summary>
    [Fact]
    public async Task A_message_from_a_ready_user_is_committed_and_confirmed()
    {
        await _app.SeedReadyUserAsync(TelegramUserId, accessToken: "gho_seeded");

        var response = await PostAsync(MessageUpdateJson("Índices parciais no Postgres #postgres"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The token came out of the database and was decrypted on the way to GitHub.
        Assert.Equal("gho_seeded", _app.GitHubRepositories.UsedAccessToken);

        var entry = _app.GitHubRepositories.WrittenEntry;
        Assert.StartsWith("til/", entry.PathPrefix, StringComparison.Ordinal);
        Assert.Equal("Índices parciais no Postgres", entry.Title);
        Assert.Equal(["postgres"], entry.Tags);

        var reply = Assert.Single(_app.Messenger.Sent).Text;
        Assert.Contains("TIL registrado", reply, StringComparison.Ordinal);
        Assert.Contains(FakeGitHubRepositoryService.EntryUrl.ToString(), reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_health_endpoint_answers()
    {
        var response = await _app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
