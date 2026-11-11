using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace SharpAstro.Firebase.Tests;

/// <summary>
/// <see cref="FirebaseDatabase"/> against a fake HTTP handler: the URLs and verbs it speaks, how it
/// signs in and refreshes, and above all how it folds an event stream into whole values. The chess
/// project's emulator tests cover the same client against a real database and its rules; these cover
/// the client's own logic, in milliseconds, with no network.
/// </summary>
public class FirebaseDatabaseTests
{
    private static readonly FirebaseConfig Real = new("the-key", "proj", "https://proj-default-rtdb.example.app");

    private static readonly FirebaseConfig Emulator = new(
        "demo", "demo-proj", "", EmulatorAuthUrl: "http://127.0.0.1:9099",
        EmulatorDatabaseHost: "127.0.0.1", EmulatorDatabasePort: 9000);

    private const string SignUpOk =
        """{"localId":"uid-1","idToken":"tok-1","refreshToken":"ref-1","expiresIn":"3600"}""";

    // A deadline for off-thread delivery, never a wait: the handler answers at once, so a value
    // arrives in microseconds and this only bounds a test that is already broken.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static FirebaseDatabase Database(FirebaseConfig config, FakeHttp http, TimeProvider? time = null) =>
        new(config, new HttpClient(http), time ?? new FakeTimeProvider());

    [Fact]
    public async Task SignIn_TakesTheUidFromIdentityToolkit_AndSendsTheKey()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json(SignUpOk));
        await using var db = Database(Real, http);

        (await db.SignInAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        db.Uid.ShouldBe("uid-1");
        var req = http.Requests.ShouldHaveSingleItem();
        req.Method.ShouldBe(HttpMethod.Post);
        req.Uri.ToString().ShouldBe("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=the-key");
        req.Body.ShouldBe("""{"returnSecureToken":true}""");
    }

    [Fact]
    public async Task SignIn_Refused_IsFalse_NotAnException()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.BadRequest));
        await using var db = Database(Real, http);

        (await db.SignInAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        db.Uid.ShouldBe("");
    }

    [Fact]
    public async Task Writes_UseTheRestVerbs_AndCarryTheToken()
    {
        var http = new FakeHttp((req, _) => req.RequestUri!.Host.StartsWith("identitytoolkit")
            ? FakeHttp.Json(SignUpOk)
            : FakeHttp.Json("{}"));
        await using var db = Database(Real, http);
        var ct = TestContext.Current.CancellationToken;
        await db.SignInAsync(ct);

        (await db.SetAsync("rows/a", """{"x":1}""", ct)).ShouldBeTrue();
        (await db.UpdateAsync("rows/a", """{"y":2}""", ct)).ShouldBeTrue();
        (await db.RemoveAsync("rows/a", ct)).ShouldBeTrue();

        var writes = http.Requests.Skip(1).ToArray();
        writes.Select(r => r.Method).ShouldBe([HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete]);
        writes.ShouldAllBe(r => r.Uri.ToString() == "https://proj-default-rtdb.example.app/rows/a.json?auth=tok-1");
        writes[0].Body.ShouldBe("""{"x":1}""");
        writes[1].Body.ShouldBe("""{"y":2}""");
    }

    [Fact]
    public async Task Emulator_PicksTheNamespaceByQuery_NotByHost()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json("""{"a":1}"""));
        await using var db = Database(Emulator, http);

        (await db.GetAsync("rows", TestContext.Current.CancellationToken)).ShouldBe("""{"a":1}""");

        http.Requests.ShouldHaveSingleItem().Uri.ToString()
            .ShouldBe("http://127.0.0.1:9000/rows.json?ns=demo-proj-default-rtdb");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "null")]          // how the REST API spells "absent"
    [InlineData(HttpStatusCode.Unauthorized, "")]    // how it spells "not yours"
    public async Task Get_AbsentAndRefused_AreBothNull(HttpStatusCode status, string body)
    {
        var http = new FakeHttp((_, _) => FakeHttp.Json(body, status));
        await using var db = Database(Real, http);

        (await db.GetAsync("rows/a", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Get_NoNetwork_IsNull_NotAnException()
    {
        var http = new FakeHttp((_, _) => throw new HttpRequestException("no route"));
        await using var db = Database(Real, http);

        (await db.GetAsync("rows/a", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Token_IsRefreshedBeforeItExpires()
    {
        var time = new FakeTimeProvider();
        var http = new FakeHttp((req, _) => req.RequestUri!.Host switch
        {
            "identitytoolkit.googleapis.com" => FakeHttp.Json(SignUpOk),
            "securetoken.googleapis.com" => FakeHttp.Json(
                """{"id_token":"tok-2","refresh_token":"ref-2","expires_in":"3600"}"""),
            _ => FakeHttp.Json("{}"),
        });
        await using var db = Database(Real, http, time);
        var ct = TestContext.Current.CancellationToken;
        await db.SignInAsync(ct);

        await db.GetAsync("a", ct);
        time.Advance(TimeSpan.FromMinutes(56)); // inside the five-minute margin of the hour
        await db.GetAsync("b", ct);

        var reqs = http.Requests;
        reqs[1].Uri.Query.ShouldContain("auth=tok-1");
        reqs[2].Uri.Host.ShouldBe("securetoken.googleapis.com");
        reqs[2].Body.ShouldBe("grant_type=refresh_token&refresh_token=ref-1");
        reqs[3].Uri.Query.ShouldContain("auth=tok-2");
    }

    [Fact]
    public async Task Watch_FoldsPutAndPatch_IntoTheWholeValue()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Events(
            ("put", """{"path":"/","data":{"g":"e2e4","n":1}}"""),
            ("keep-alive", "null"),
            ("patch", """{"path":"/","data":{"g":"e2e4 e7e5","n":2}}"""),
            ("put", """{"path":"/who","data":"w"}"""),
            ("patch", """{"path":"/","data":{"who":null}}""")));
        await using var db = Database(Real, http);
        var values = Channel.CreateUnbounded<string?>();

        using var _ = db.Watch("games/x", v => values.Writer.TryWrite(v));

        (await Next(values)).ShouldBe("""{"g":"e2e4","n":1}""");
        (await Next(values)).ShouldBe("""{"g":"e2e4 e7e5","n":2}""");
        (await Next(values)).ShouldBe("""{"g":"e2e4 e7e5","n":2,"who":"w"}""");
        (await Next(values)).ShouldBe("""{"g":"e2e4 e7e5","n":2}""");

        var stream = http.Requests.ShouldHaveSingleItem();
        stream.Accept.ShouldBe("text/event-stream");
        stream.Uri.AbsolutePath.ShouldBe("/games/x.json");
    }

    [Fact]
    public async Task Watch_ARefusedStream_DeliversNull()
    {
        var http = new FakeHttp((_, _) => FakeHttp.Status(HttpStatusCode.Forbidden));
        await using var db = Database(Real, http);
        var values = Channel.CreateUnbounded<string?>();

        using var _ = db.Watch("games/x", v => values.Writer.TryWrite(v));

        (await Next(values)).ShouldBeNull();
    }

    [Fact]
    public async Task Watch_AChangeDeeperThanItFolds_ReadsThePathInstead()
    {
        var http = new FakeHttp((req, _) => req.Headers.Accept.Any(a => a.MediaType == "text/event-stream")
            ? FakeHttp.Events(
                ("put", """{"path":"/","data":{"a":{"b":1}}}"""),
                ("put", """{"path":"/a/b","data":2}"""))
            : FakeHttp.Json("""{"a":{"b":2}}"""));
        await using var db = Database(Real, http);
        var values = Channel.CreateUnbounded<string?>();

        using var _ = db.Watch("rows", v => values.Writer.TryWrite(v));

        (await Next(values)).ShouldBe("""{"a":{"b":1}}""");
        (await Next(values)).ShouldBe("""{"a":{"b":2}}""");
        http.Requests.Count(r => r.Accept is null).ShouldBe(1); // the one fallback GET
    }

    private static async Task<string?> Next(Channel<string?> values) =>
        await values.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Deadline);
}
