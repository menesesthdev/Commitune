using System.Net;
using System.Text.Json;
using Commitune.Infrastructure.GitHub;
using Octokit;
using Octokit.Internal;

namespace Commitune.Tests.GitHub;

/// <summary>
/// Guards the one rule in CLAUDE.md that has no acceptable exception: a repository created by
/// Commitune is private. Asserted against the JSON actually put on the wire, not against the
/// object we handed Octokit — a mapping change that dropped the flag would still be caught.
/// </summary>
public class GitHubRepositoryServiceTests
{
    private const string AccessToken = "gho_notARealTokenJustForTests";

    private const string CreatedRepositoryJson = """
        {"id":1,"name":"diario","full_name":"tester/diario","private":true,
         "owner":{"id":2,"login":"tester"}}
        """;

    private static (GitHubRepositoryService Service, FakeHttpMessageHandler Handler) CreateService(
        Action<FakeHttpMessageHandler> configure)
    {
        var handler = new FakeHttpMessageHandler();
        configure(handler);

        var connection = new Connection(
            new ProductHeaderValue("Commitune-tests"),
            new Uri("https://api.github.com"),
            new InMemoryCredentialStore(new Credentials(AccessToken)),
            new HttpClientAdapter(() => handler),
            new SimpleJsonSerializer());

        return (new GitHubRepositoryService(new StubClientFactory(new GitHubClient(connection))), handler);
    }

    [Fact]
    public async Task Sends_private_true_when_creating_the_repository()
    {
        var (service, handler) = CreateService(h => h.Respond(HttpStatusCode.Created, CreatedRepositoryJson));

        await service.CreatePrivateRepositoryAsync(AccessToken, "diario", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/user/repos", request.Uri.AbsolutePath);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.True(body.RootElement.GetProperty("private").GetBoolean());
    }

    [Fact]
    public async Task Asks_github_to_initialize_the_repository()
    {
        // Without a base commit there is no sha for the Contents API to write against.
        var (service, handler) = CreateService(h => h.Respond(HttpStatusCode.Created, CreatedRepositoryJson));

        await service.CreatePrivateRepositoryAsync(AccessToken, "diario", CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.True(body.RootElement.GetProperty("auto_init").GetBoolean());
    }

    [Fact]
    public async Task Returns_the_owner_and_name_github_actually_created()
    {
        var (service, _) = CreateService(h => h.Respond(HttpStatusCode.Created, CreatedRepositoryJson));

        var repository = await service.CreatePrivateRepositoryAsync(AccessToken, "diario", CancellationToken.None);

        Assert.Equal("tester", repository.Owner);
        Assert.Equal("diario", repository.Name);
    }

    [Fact]
    public async Task Surfaces_a_name_already_in_use_as_RepositoryExistsException()
    {
        var (service, _) = CreateService(h => h.Respond(
            HttpStatusCode.UnprocessableEntity,
            """
            {"message":"Repository creation failed.",
             "errors":[{"resource":"Repository","code":"custom","field":"name",
                        "message":"name already exists on this account"}]}
            """));

        await Assert.ThrowsAsync<RepositoryExistsException>(
            () => service.CreatePrivateRepositoryAsync(AccessToken, "diario", CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_to_call_github_without_a_name()
    {
        var (service, handler) = CreateService(_ => { });

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.CreatePrivateRepositoryAsync(AccessToken, "  ", CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    private sealed class StubClientFactory(IGitHubClient client) : IGitHubClientFactory
    {
        public IGitHubClient Create(string accessToken) => client;
    }
}
