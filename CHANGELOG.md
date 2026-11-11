# Changelog

One entry per `VersionMajorMinor`, newest first. Write the entry in the SAME commit as the bump: merging to `main`
publishes, so a consumer on a floating `X.Y.*` pin has no other warning. Say what changed, what is NOT included,
and flag any behaviour change plainly.

## 1.0

First release, lifted out of the chess project's `Chess.Net/Cloud` (sebgod/chess), where it has run since
2026-09 against the live database and the emulator.

- `ICloudDatabase`, `FirebaseDatabase`, `FirebaseConfig`: the client as chess shipped it, namespace
  `SharpAstro.Firebase` instead of `Chess.Net.Cloud`. No behaviour change.
- `CloudJson` is now **public** (it was internal to Chess.Net): an app reading and writing its own rows wants the
  same fallback-not-throw readers.
- `InMemoryCloudDatabase` / `InMemoryCloudStore`: chess's test fake (`FakeCloudDatabase` / `FakeCloudStore`),
  made public so every consumer tests against the same leaf-tree model instead of writing its own.
- Unit tests for the client against a fake HTTP handler (sign-in, verbs, URLs, token refresh, event-stream
  folding), which until now only the emulator suite covered.

NOT included: the lobby, invites, presence or any request/answer layer (they stay in the apps until a second one
needs the same shape), and the emulator tests, which test chess's security rules and stay in chess.
