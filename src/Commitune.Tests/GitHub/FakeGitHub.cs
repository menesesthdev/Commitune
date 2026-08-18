using Commitune.Infrastructure.GitHub;
using Octokit;
using Octokit.Internal;

namespace Commitune.Tests.GitHub;

/// <summary>
/// A real Octokit client wired to canned HTTP responses. Going through Octokit's own
/// serialization rather than a stubbed <see cref="IGitHubClient"/> is the point: what these
/// tests assert is the request that actually reaches GitHub.
/// </summary>
internal static class FakeGitHub
{
    public const string AccessToken = "gho_notARealTokenJustForTests";

    public static (GitHubRepositoryService Service, FakeHttpMessageHandler Handler) CreateService(
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

    private sealed class StubClientFactory(IGitHubClient client) : IGitHubClientFactory
    {
        public IGitHubClient Create(string accessToken) => client;
    }
}
