# SharpAstro.Firebase

A Firebase **Realtime Database** client for .NET with **no SDK and no package dependencies**, AOT and trim clean.
Plus an in-memory database that behaves like the real one, for testing an app's cloud logic with no cloud.

Lifted out of the chess project's correspondence play (`sebgod/chess`, `Chess.Net/Cloud`), where it has carried
games between the browser and native clients since 2026-09. It is the shared plumbing for reaching an app from
outside its LAN through a relay both ends dial out to (SharpAstro/.github#9): tianwen's rig control from the
website (SharpAstro/tianwen#1815) and pilot's remote nodes (SharpAstro/pilot#774) build on it.

## What is in it

| Type | What it is |
|---|---|
| `ICloudDatabase` | Read, write, merge, remove and watch a path. Values are raw JSON. |
| `FirebaseDatabase` | The real one: REST (`path.json`), a server-sent-event stream (`Accept: text/event-stream`) folded into whole values, anonymous sign-in with token refresh, silent reconnect with backoff. |
| `FirebaseConfig` | Parses the Firebase web config JSON. Absent or partial config is `null`, never a throw. Takes `apiKeyNative` over `apiKey`, and an `emulator` block. |
| `CloudJson` | Fallback-not-throw readers over `JsonDocument`, and the two folds the event stream uses. |
| `InMemoryCloudDatabase` / `InMemoryCloudStore` | An in-memory database that stores a **leaf tree** like RTDB: an empty object does not exist, a path with no leaves reads null, a set replaces a subtree. Synchronous delivery, a `Refuse` hook standing in for the rules, server timestamps from a `TimeProvider`. |

What is **not** in it: any app's schema or rules, a lobby or invite handshake, presence, encryption. Those belong
to the app, or to a later layer once a second app needs the same one.

## Use

```csharp
var config = FirebaseConfig.TryParse(json);       // null: no cloud, carry on without it
if (config is null) return;

await using var db = new FirebaseDatabase(config);
if (!await db.SignInAsync()) return;              // false: no cloud right now, never an exception

await db.SetAsync($"rows/{db.Uid}", """{"hello":"world"}""");
using var watch = db.Watch("rows", json => Console.WriteLine(json));   // current value first, then every change
```

## The contract, in short

- **Absent and refused are one answer** (`null`), because a caller does the same thing about both.
- **A write the rules refuse is `false`**, a legitimate outcome rather than a failure.
- **Network trouble is never "gone".** A dropped stream backs off and reconnects without telling the caller; only
  the server saying null, or refusing the read, delivers null.
- **An id token lasts an hour** and is refreshed five minutes before it runs out.

## Two API keys, one config

A browser key restricted by HTTP referrer refuses every native client, because `HttpClient` sends no `Referer`
(measured: 403). So a native front end needs its own key. Put it in the same config as `apiKeyNative`; this client
prefers it and falls back to `apiKey`.

## Spark, not Blaze

Run the project on the free **Spark** plan with **no billing account**: over a limit, Spark refuses service
instead of invoicing, and for a write endpoint on the open internet that is the whole risk model. One project per
app. Provisioning and the audit live in SharpAstro/cloud-ops.

## Build

```bash
dotnet build SharpAstro.Firebase.slnx -c Release
dotnet test --project src/SharpAstro.Firebase.Tests -c Release --no-build
```

MIT licensed.
