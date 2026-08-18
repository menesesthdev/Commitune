using System.Net;
using System.Text;
using System.Text.Json;
using Commitune.Infrastructure.GitHub;
using Octokit;

// Octokit has a RepositoryReference of its own; ours is the one meant here.
using RepositoryReference = Commitune.Infrastructure.GitHub.RepositoryReference;

namespace Commitune.Tests.GitHub;

/// <summary>
/// The write path, asserted against the JSON that reaches the Contents API. The case worth the
/// effort is two entries resolving to the same file name: one entry must never land on top of
/// another, and neither may be lost.
/// </summary>
public class GitHubRepositoryServiceCommitTests
{
    private const string AccessToken = FakeGitHub.AccessToken;

    private const string PathPrefix = "til/2026-08-18-indices-parciais";

    private static readonly RepositoryReference Repository = new("tester", "til");

    private static readonly TilEntry Entry = new(
        PathPrefix,
        Title: "Índices parciais",
        Tags: ["postgres"],
        Content: "---\ntitle: \"Índices parciais\"\n---\n\n# Índices parciais\n",
        CommitMessage: "TIL: Índices parciais");

    private const string NotFoundJson = """{"message":"Not Found"}""";

    private const string ConflictJson = """{"message":"Invalid request.\n\n\"sha\" wasn't supplied."}""";

    private const string CommitResponseJson = """
        {"content":{"name":"2026-08-18-indices-parciais.md","path":"til/2026-08-18-indices-parciais.md",
                    "sha":"novo",
                    "html_url":"https://github.com/tester/til/blob/main/til/2026-08-18-indices-parciais.md"},
         "commit":{"sha":"c0ffee","message":"TIL: Índices parciais"}}
        """;

    /// <summary>The response GitHub gives for a file that is already there.</summary>
    private static string FileJson(string path) => JsonSerializer.Serialize(new
    {
        type = "file",
        name = path.Split('/')[^1],
        path,
        sha = "ja-existe",
        size = 10,
        encoding = "base64",
        content = Convert.ToBase64String("já escrito"u8.ToArray()),
    });

    private static string PathOf(FakeHttpMessageHandler.RecordedRequest request)
        => request.Uri.AbsolutePath.Replace("/repos/tester/til/contents/", string.Empty, StringComparison.Ordinal);

    private static string SentContent(FakeHttpMessageHandler.RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);

        return Encoding.UTF8.GetString(
            Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!));
    }

    [Fact]
    public async Task Writes_the_entry_as_a_new_file()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal(HttpMethod.Put, write.Method);
        Assert.Equal("til/2026-08-18-indices-parciais.md", PathOf(write));
        Assert.Equal(Entry.Content, SentContent(write));
    }

    [Fact]
    public async Task Returns_the_commit_and_the_link_to_the_file()
    {
        var (service, _) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        var committed = await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        Assert.Equal("c0ffee", committed.CommitSha);
        Assert.Equal("til/2026-08-18-indices-parciais.md", committed.Path);
        Assert.Equal(
            "https://github.com/tester/til/blob/main/til/2026-08-18-indices-parciais.md",
            committed.Url?.ToString());
    }

    /// <summary>
    /// Two TILs about the same topic on the same day. The second one gets its own file — the
    /// alternative is overwriting an entry the user believes is saved.
    /// </summary>
    [Fact]
    public async Task A_second_entry_on_the_same_topic_and_day_gets_its_own_file()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.OK, FileJson($"{PathPrefix}.md"))
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        var committed = await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal(HttpMethod.Put, write.Method);
        Assert.Equal("til/2026-08-18-indices-parciais-2.md", PathOf(write));
        Assert.Equal("til/2026-08-18-indices-parciais-2.md", committed.Path);
    }

    [Fact]
    public async Task Keeps_looking_while_the_names_are_taken()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.OK, FileJson($"{PathPrefix}.md"))
            .Respond(HttpStatusCode.OK, FileJson($"{PathPrefix}-2.md"))
            .Respond(HttpStatusCode.OK, FileJson($"{PathPrefix}-3.md"))
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        Assert.Equal("til/2026-08-18-indices-parciais-4.md", PathOf(handler.Requests[^1]));
    }

    /// <summary>
    /// The race the check cannot close: the name was free when we looked and taken by the time
    /// we wrote. GitHub answers 422, and the entry moves to the next name instead of failing.
    /// </summary>
    [Fact]
    public async Task Moves_to_the_next_name_when_another_message_took_this_one_mid_write()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.UnprocessableEntity, ConflictJson)
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        Assert.Equal("til/2026-08-18-indices-parciais-2.md", PathOf(handler.Requests[^1]));
    }

    [Fact]
    public async Task Gives_up_when_every_name_is_taken()
    {
        var (service, _) = FakeGitHub.CreateService(h =>
        {
            for (var attempt = 1; attempt <= GitHubRepositoryService.MaxPathAttempts; attempt++)
            {
                h.Respond(HttpStatusCode.OK, FileJson($"{PathPrefix}-{attempt}.md"));
            }
        });

        // Distinct from a GitHub failure: there is nothing to retry, and the bot says so.
        await Assert.ThrowsAsync<EntryPathUnavailableException>(
            () => service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None));
    }

    /// <summary>
    /// A deleted repository answers 404 to the check, exactly like a free name does. The create
    /// that follows is what tells the two apart — and it must surface, not be swallowed.
    /// </summary>
    [Fact]
    public async Task Surfaces_a_missing_repository_instead_of_pretending_the_name_was_free()
    {
        var (service, _) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.NotFound, NotFoundJson));

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None));
    }

    [Fact]
    public async Task Refuses_to_call_github_without_a_path()
    {
        var (service, handler) = FakeGitHub.CreateService(_ => { });

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.CommitEntryAsync(
                AccessToken, Repository, Entry with { PathPrefix = " " }, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }
}
