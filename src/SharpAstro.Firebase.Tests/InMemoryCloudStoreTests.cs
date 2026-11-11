using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace SharpAstro.Firebase.Tests;

/// <summary>
/// The in-memory database models RTDB's leaf tree, not opaque keys. These pin the three semantics
/// that make that matter (an empty object does not exist, a path with no leaves reads null, a set
/// replaces a subtree) plus the watch contract every consumer's tests rely on.
/// </summary>
public class InMemoryCloudStoreTests
{
    private static (InMemoryCloudStore Store, InMemoryCloudDatabase Db) New()
    {
        var store = new InMemoryCloudStore();
        return (store, new InMemoryCloudDatabase(store, "me"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmptyObject_DoesNotExist()
    {
        var (_, db) = New();
        await db.SetAsync("rows/a", "{}", Ct);

        (await db.GetAsync("rows/a", Ct)).ShouldBeNull();
        (await db.GetAsync("rows", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Set_ReplacesTheSubtree_Update_MergesIntoIt()
    {
        var (_, db) = New();
        await db.SetAsync("rows/a", """{"x":1,"y":2}""", Ct);

        await db.UpdateAsync("rows/a", """{"y":3,"z":4}""", Ct);
        (await db.GetAsync("rows/a", Ct)).ShouldBe("""{"x":1,"y":3,"z":4}""");

        await db.SetAsync("rows/a", """{"w":5}""", Ct);
        (await db.GetAsync("rows/a", Ct)).ShouldBe("""{"w":5}""");
    }

    [Fact]
    public async Task ANullMember_InAnUpdate_Deletes()
    {
        var (_, db) = New();
        await db.SetAsync("rows/a", """{"x":1,"y":2}""", Ct);

        await db.UpdateAsync("rows/a", """{"y":null}""", Ct);

        (await db.GetAsync("rows/a", Ct)).ShouldBe("""{"x":1}""");
    }

    [Fact]
    public async Task Watch_DeliversTheCurrentValueFirst_ThenChangesAboveAndBelow()
    {
        var (_, db) = New();
        await db.SetAsync("rows/a/x", "1", Ct);
        var seen = new List<string?>();

        using var sub = db.Watch("rows/a", seen.Add);
        await db.SetAsync("rows/a/y", "2", Ct);   // below the watch
        await db.RemoveAsync("rows", Ct);         // above it

        seen.ShouldBe(["""{"x":1}""", """{"x":1,"y":2}""", null]);
    }

    [Fact]
    public async Task ADisposedWatch_HearsNothing()
    {
        var (_, db) = New();
        var seen = new List<string?>();

        db.Watch("rows", seen.Add).Dispose();
        await db.SetAsync("rows/a", "1", Ct);

        seen.ShouldBe([null]); // only the first delivery, from before the dispose
    }

    [Fact]
    public async Task Refuse_StandsInForTheRules_AndWritesNothing()
    {
        var (store, db) = New();
        store.Refuse = (path, _) => path == "seats/white";

        (await db.SetAsync("seats/white", "\"me\"", Ct)).ShouldBeFalse();
        (await db.GetAsync("seats/white", Ct)).ShouldBeNull();
        store.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoClients_OfOneStore_SeeEachOther()
    {
        var store = new InMemoryCloudStore();
        await using var alice = new InMemoryCloudDatabase(store, "alice");
        await using var bob = new InMemoryCloudDatabase(store, "bob");
        await alice.SignInAsync(Ct);
        await bob.SignInAsync(Ct);
        var seen = new List<string?>();

        using var sub = bob.Watch("inbox", seen.Add);
        await alice.SetAsync("inbox/m1", "\"hi\"", Ct);

        alice.Uid.ShouldBe("alice");
        seen.Last().ShouldBe("""{"m1":"hi"}""");
    }

    [Fact]
    public async Task AServerTimestamp_IsTheStoresClock()
    {
        var (store, db) = New();
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        store.Time = time;

        await db.SetAsync("rows/a/updated", """{".sv":"timestamp"}""", Ct);

        (await db.GetAsync("rows/a/updated", Ct)).ShouldBe("1700000000000");
    }
}
