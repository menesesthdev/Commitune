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
/// effort is the second message of the same minute: two writes to one file, where a naive
/// implementation quietly overwrites the entry that got there first.
/// </summary>
public class GitHubRepositoryServiceCommitTests
{
    private const string AccessToken = FakeGitHub.AccessToken;

    private const string Path = "diario/2026/08/2026-08-18.md";
    private const string ContentsPath = "/repos/tester/diario/contents/diario/2026/08/2026-08-18.md";

    private static readonly RepositoryReference Repository = new("tester", "diario");

    private static readonly DiaryEntry Entry = new(
        Path,
        NewFileContent: "# 18/08/2026\n\n## 22:30\n\nminha entrada\n",
        AppendedBlock: "## 22:30\n\nminha entrada\n",
        CommitMessage: "Entrada de 18/08/2026 às 22:30");

    private const string NotFoundJson = """{"message":"Not Found"}""";

    private const string CommitResponseJson = """
        {"content":{"name":"2026-08-18.md","path":"diario/2026/08/2026-08-18.md","sha":"novo",
                    "html_url":"https://github.com/tester/diario/blob/main/diario/2026/08/2026-08-18.md"},
         "commit":{"sha":"c0ffee","message":"Entrada"}}
        """;

    /// <summary>The response GitHub gives for an existing file: the body, base64, plus its sha.</summary>
    private static string FileJson(string content, string sha) => JsonSerializer.Serialize(new
    {
        type = "file",
        name = "2026-08-18.md",
        path = Path,
        sha,
        size = content.Length,
        encoding = "base64",
        content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
    });

    private static string SentContent(FakeHttpMessageHandler.RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);

        return Encoding.UTF8.GetString(
            Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!));
    }

    private static string? SentSha(FakeHttpMessageHandler.RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);

        return body.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
    }

    [Fact]
    public async Task Creates_the_days_file_on_the_first_entry_of_the_day()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal(HttpMethod.Put, write.Method);
        Assert.Equal(ContentsPath, write.Uri.AbsolutePath);
        Assert.Equal(Entry.NewFileContent, SentContent(write));

        // No sha means "this file does not exist yet" — sending one would be a lie GitHub rejects.
        Assert.Null(SentSha(write));
    }

    [Fact]
    public async Task Appends_to_the_day_already_started_instead_of_replacing_it()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.OK, FileJson("# 18/08/2026\n\n## 09:00\n\nde manhã\n", "sha-atual"))
            .Respond(HttpStatusCode.OK, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal("# 18/08/2026\n\n## 09:00\n\nde manhã\n\n## 22:30\n\nminha entrada\n", SentContent(write));
        Assert.Equal("sha-atual", SentSha(write));
    }

    [Fact]
    public async Task Returns_the_commit_and_the_link_to_the_file()
    {
        var (service, _) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Created, CommitResponseJson));

        var committed = await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        Assert.Equal("c0ffee", committed.CommitSha);
        Assert.Equal(
            "https://github.com/tester/diario/blob/main/diario/2026/08/2026-08-18.md",
            committed.Url?.ToString());
    }

    /// <summary>
    /// Two messages seconds apart: the sha read here was already replaced by the other write.
    /// The retry has to re-read, so the entry that won the race survives ours.
    /// </summary>
    [Fact]
    public async Task Refetches_and_retries_when_the_sha_went_stale()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.OK, FileJson("# 18/08/2026\n\n## 09:00\n\nde manhã\n", "sha-velho"))
            .Respond(HttpStatusCode.Conflict, """{"message":"is at 9c1... but expected 4a2..."}""")
            .Respond(HttpStatusCode.OK, FileJson("# 18/08/2026\n\n## 09:00\n\nde manhã\n\n## 22:29\n\na outra\n", "sha-novo"))
            .Respond(HttpStatusCode.OK, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal("sha-novo", SentSha(write));

        var content = SentContent(write);
        Assert.Contains("## 22:29\n\na outra", content, StringComparison.Ordinal);
        Assert.EndsWith("## 22:30\n\nminha entrada\n", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same race, other order: we read a 404 and tried to create, but the other write got the
    /// file in first. GitHub answers 422 ("sha wasn't supplied"), and the fix is the same.
    /// </summary>
    [Fact]
    public async Task Retries_as_an_update_when_the_file_appeared_while_we_were_creating_it()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.UnprocessableEntity, """{"message":"Invalid request.\n\n\"sha\" wasn't supplied."}""")
            .Respond(HttpStatusCode.OK, FileJson("# 18/08/2026\n\n## 22:29\n\na outra\n", "sha-do-outro"))
            .Respond(HttpStatusCode.OK, CommitResponseJson));

        await service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None);

        var write = handler.Requests[^1];
        Assert.Equal("sha-do-outro", SentSha(write));
        Assert.Contains("a outra", SentContent(write), StringComparison.Ordinal);
    }

    /// <summary>
    /// One retry, not a loop: a conflict that survives a re-read is not a race, and the user is
    /// better served by hearing that it failed than by us hammering GitHub.
    /// </summary>
    [Fact]
    public async Task Gives_up_after_one_retry()
    {
        var (service, handler) = FakeGitHub.CreateService(h => h
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Conflict, """{"message":"conflict"}""")
            .Respond(HttpStatusCode.NotFound, NotFoundJson)
            .Respond(HttpStatusCode.Conflict, """{"message":"conflict"}"""));

        await Assert.ThrowsAnyAsync<ApiException>(
            () => service.CommitEntryAsync(AccessToken, Repository, Entry, CancellationToken.None));

        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// A deleted repository answers 404 to the read, exactly like a new day does. The create
    /// that follows is what tells the two apart — and it must surface, not be swallowed.
    /// </summary>
    [Fact]
    public async Task Surfaces_a_missing_repository_instead_of_pretending_the_day_was_new()
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
            () => service.CommitEntryAsync(AccessToken, Repository, Entry with { Path = " " }, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }
}
