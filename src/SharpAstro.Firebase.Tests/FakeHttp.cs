using System.Net;
using System.Text;

namespace SharpAstro.Firebase.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a function and records what it was asked, so
/// <see cref="FirebaseDatabase"/> is tested with no network. An event stream is a response whose body
/// is the SSE text; when the body ends the client reconnects only after a backoff on its
/// <see cref="TimeProvider"/>, which a test holds still with a <c>FakeTimeProvider</c>, so nothing
/// reconnects behind the test's back.
/// </summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<(HttpMethod Method, Uri Uri, string? Body, string? Accept)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string? Body, string? Accept)> Requests
    {
        get { lock (_gate) return _requests.ToArray(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (_gate)
        {
            _requests.Add((request.Method, request.RequestUri!, body,
                request.Headers.Accept.FirstOrDefault()?.MediaType));
        }
        return answer(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    /// <summary>An event-stream body: each item is one <c>(event, data)</c> frame.</summary>
    public static HttpResponseMessage Events(params (string Event, string Data)[] frames)
    {
        var sb = new StringBuilder();
        foreach (var (e, d) in frames) sb.Append("event: ").Append(e).Append('\n').Append("data: ").Append(d).Append("\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }
}
