# Contributing

Thanks for your interest in contributing to Slashcoded Desktop Tracker.

## Reporting issues

Please open an issue with:

- A short, clear title.
- Steps to reproduce.
- Expected vs. actual behavior.
- Logs or screenshots when relevant.
- Your OS version and the tracker version/commit.

If the issue involves timing, idle behavior, or segment size, also include:

- The current host tracking config version from `trackerConfigVersion` payload metadata.
- Whether the issue happened before the first successful config fetch or after a refresh.
- The observed `segmentDurationSeconds` and `idleThresholdSeconds` values in uploaded payloads.

If the issue involves the local API, include the API version and any relevant request/response details.

## Pull requests

- Keep changes focused and scoped.
- Add or update documentation when behavior changes.
- Run `dotnet build` and `dotnet test` before opening a PR.
