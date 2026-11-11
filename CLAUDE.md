# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`SharpAstro.Firebase`: a Firebase Realtime Database client with no SDK and no package dependencies (REST +
server-sent events + anonymous auth over `HttpClient`), AOT/trim clean, plus an in-memory database that models
RTDB's leaf tree for tests. See `README.md` for the contract. Lifted out of chess's `Chess.Net/Cloud`; the shared
relay plumbing for chess, tianwen and pilot (SharpAstro/.github#9).

## Commands

```bash
dotnet build SharpAstro.Firebase.slnx -c Release
dotnet test --project src/SharpAstro.Firebase.Tests -c Release --no-build
dotnet test --project src/SharpAstro.Firebase.Tests -c Release --no-build --filter-class "*FirebaseDatabaseTests"
```

Tests are xunit.v3 4.x with Shouldly on **Microsoft.Testing.Platform** (`global.json` opts `dotnet test` in), not
VSTest: `--logger` and `--blame-*` fail the run.

## Rules

- **No package dependencies in the library.** Everything it needs ships in the shared framework. A consumer that is
  AOT (tianwen, chess's Android head) pays nothing for it. Do not add one without a reason that survives that.
- **No reflection-based JSON.** Read with `JsonDocument` through `CloudJson`, write by string building with
  `CloudJson.Quote` / `JsonEncodedText`.
- **No app's schema here.** Paths and rules are the app's. A lobby, invites, presence or a request/answer layer moves
  in only when a second app needs the same shape.
- **Never throw on network trouble.** Absent and refused are `null`, a refused write is `false`, a dropped stream
  reconnects silently. Only the server saying null (or refusing the read) reaches the caller as null.
- **Tests never touch a network or a wall clock.** `FakeHttp` answers HTTP; `FakeTimeProvider` holds time still, so
  a stream that ends does not reconnect behind the test. Off-thread delivery is awaited through a channel with a
  deadline, never polled. Tests against a real database (the emulator) belong to the app whose rules they test.

## Versioning and release

- The Major.Minor lives in exactly one place, `VersionMajorMinor` in the repo-root `Directory.Build.props`; CI reads
  it back with `dotnet msbuild Directory.Build.props -getProperty:VersionMajorMinor`. Minor for additive, major for
  breaking.
- **Merging to `main` publishes** to nuget.org as `X.Y.<run><attempt>`. Write the `CHANGELOG.md` entry in the same
  commit as the bump.
- main requires linear history: land by squash or rebase.

## Writing

No em-dashes in new prose, comments or commit messages: a full stop, a comma, a semicolon or parentheses. (Comments
carried over from chess keep theirs until they are next edited.)
