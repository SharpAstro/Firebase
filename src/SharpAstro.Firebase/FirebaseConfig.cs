using System;
using System.Text.Json;

namespace SharpAstro.Firebase;

/// <summary>
/// The deployment's backend, parsed out of the Firebase web config JSON (the same
/// <c>firebase-config.json</c> a web app is handed at deploy time, in chess from its
/// <c>FIREBASE_CONFIG</c> repo secret). It is deployment state rather than build state (the same
/// binary serves a fork with no backend and the real thing with one), so it is read at runtime and
/// <b>absent is a normal state</b>: no config means no cloud, the app carries on without it, and
/// nothing here throws.
///
/// <para><b>The API key is per-front-end and this is where that lands.</b> A browser key is
/// restricted to its site (chess's to <c>sebgod.github.io/*</c>), and an HTTP-referrer restriction
/// refuses a request that sends no <c>Referer</c>, which is every native client: the web key fails at
/// sign-in with a 403 that reads like a broken auth implementation. The native key is therefore a
/// second, unrestricted one, carried as an override in the same config: <c>apiKeyNative</c> when
/// present, <c>apiKey</c> otherwise. One field beats a second copy of five that must not drift, and
/// a config with no override still works against the emulator, which validates no key at all.</para>
/// </summary>
public sealed record FirebaseConfig(
    string ApiKey,
    string ProjectId,
    string DatabaseUrl,
    string? EmulatorAuthUrl = null,
    string? EmulatorDatabaseHost = null,
    int EmulatorDatabasePort = 0)
{
    /// <summary>True when this points at a local emulator rather than a real project: the whole cloud
    /// path is then exercisable with no secret, no account and no network (see firebase/).</summary>
    public bool IsEmulator => !string.IsNullOrEmpty(EmulatorAuthUrl);

    /// <summary>
    /// Parse a config, or null when there isn't one. Null covers every "no cloud" case — an empty
    /// string, a file that is really the dev server's HTML fallback, a config missing the fields a
    /// client cannot work without — because a caller has exactly one thing to do about all of them.
    /// </summary>
    public static FirebaseConfig? TryParse(string? json)
    {
        using var doc = CloudJson.TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        var root = doc.RootElement;
        var emulator = CloudJson.Child(root, "emulator");

        var apiKey = CloudJson.StringOr(root, "apiKeyNative", CloudJson.StringOr(root, "apiKey"));
        var projectId = CloudJson.StringOr(root, "projectId");
        var databaseUrl = CloudJson.StringOr(root, "databaseURL").TrimEnd('/');

        // An emulator config carries no key, because there is no project to have one — but the
        // emulator still REFUSES a request with an empty `key` ("The request is missing a valid API
        // key", 403), and any non-empty string satisfies it. A web client on the JS SDK passes "demo"
        // for the same reason (chess's does); matching it keeps one answer to "what key is this".
        if (apiKey.Length == 0 && emulator.ValueKind == JsonValueKind.Object) apiKey = "demo";

        // Against the emulator the SDK-shaped fields are mostly absent on purpose (the web config
        // carries only a "demo-" projectId), so only the project id is genuinely required there.
        if (projectId.Length == 0) return null;
        if (emulator.ValueKind != JsonValueKind.Object && (apiKey.Length == 0 || databaseUrl.Length == 0))
            return null;

        return new FirebaseConfig(
            apiKey,
            projectId,
            databaseUrl,
            EmulatorAuthUrl: CloudJson.StringOr(emulator, "auth").TrimEnd('/') is { Length: > 0 } a ? a : null,
            EmulatorDatabaseHost: CloudJson.StringOr(emulator, "host") is { Length: > 0 } h ? h : null,
            EmulatorDatabasePort: CloudJson.IntOr(emulator, "port"));
    }
}
