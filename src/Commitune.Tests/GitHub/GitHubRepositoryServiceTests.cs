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
    private const string AccessToken = FakeGitHub.AccessToken;

    private const string CreatedRepositoryJson = """
        {"id":1,"name":"diario","full_name":"tester/diario","private":true,
         "owner":{"id":2,"login":"tester"}}
        """;

    private static (GitHubRepositoryService Service, FakeHttpMessageHandler Handler) CreateService(
        Action<FakeHttpMessageHandler> configure)
        => FakeGitHub.CreateService(configure);

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

    /// <summary>
    /// The lookup behind "point me at a repository I already have". What it reports about
    /// visibility is what keeps entries out of a public repository, so it is read off the wire.
    /// </summary>
    [Fact]
    public async Task Reports_the_visibility_of_an_existing_repository()
    {
        var (service, handler) = CreateService(h => h.Respond(HttpStatusCode.OK, """
            {"id":1,"name":"blog","full_name":"tester/blog","private":false,
             "owner":{"id":2,"login":"tester"}}
            """));

        var existing = await service.FindRepositoryAsync(AccessToken, "tester", "blog", CancellationToken.None);

        Assert.Equal("/repos/tester/blog", Assert.Single(handler.Requests).Uri.AbsolutePath);
        Assert.False(existing!.Value.IsPrivate);
        Assert.Equal("tester", existing.Value.Reference.Owner);
    }

    [Fact]
    public async Task Reports_nothing_when_there_is_no_such_repository()
    {
        var (service, _) = CreateService(h => h.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Assert.Null(await service.FindRepositoryAsync(AccessToken, "tester", "til", CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_to_call_github_without_a_name()
    {
        var (service, handler) = CreateService(_ => { });

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.CreatePrivateRepositoryAsync(AccessToken, "  ", CancellationToken.None));

        Assert.Empty(handler.Requests);
    }
}
