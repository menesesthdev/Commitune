using System.Net;

namespace Commitune.Tests.GitHub;

/// <summary>
/// Answers with canned responses and keeps every request, body included, so tests can assert
/// what was actually sent over the wire.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Respond(HttpStatusCode status, string body, string contentType = "application/json")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        });

        return this;
    }

    public FakeHttpMessageHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.ToString()));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No canned response left for {request.Method} {request.RequestUri}.");
        }

        return _responses.Dequeue()(request);
    }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);
}
