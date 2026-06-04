# Changelog

## Unreleased

- Added support for desktop policy flags `measureEnabled` and `discoveryEnabled`.
- Updated desktop policy parsing to prefer `allowedApps` and `ignoredApps` while preserving legacy `apps` responses.
- Ensured measurement-off policy changes discard active segments without upload.
- Documented and tested immediate unknown-app discovery on first foreground observation.

## 0.4.0 - 2026-05-23

- Rebranded the Windows desktop app from Desktop Tracker to Desktop Observer across project names, namespaces, docs, logs, and configuration.
- Renamed the build outputs to `Slashcoded.DesktopObserver` and the solution to `slashcoded-windows-observer.sln`.
- Changed trusted source enrollment to `clientId = "desktop-observer"` and `displayName = "Windows App Observer"`.
- Added `Observer` as the preferred configuration section while keeping a fallback to the legacy `Tracker` section for existing installs.
- Preserved upload contract compatibility for ingestion fields such as `producer`, `timezoneSource`, and `trackerConfigVersion`.

## 0.3.0 - 2026-05-05

- Switched desktop uploads to the flat `contractVersion = "v3"` app event contract.
- Removed redundant nested payload fields from uploads, including process aliases, process path, segment timestamps, and deterministic event id.
- Kept process identity, display name, timezone metadata, duration, and observer timing diagnostics as top-level event fields.

## 0.2.0 - 2026-04-14

- Added shared host timing integration via `/api/host/handshake` and `/api/host/tracking-config`.
- Enforced shared segment duration and idle cutoff in desktop foreground app event emission.
- Added diagnostic payload metadata for timing parity checks: `trackerConfigVersion`, `segmentDurationSeconds`, and `idleThresholdSeconds`.
- Added automated tests for host config fallback, event payload timing, idle cutoff, segment boundaries, and config refresh.
