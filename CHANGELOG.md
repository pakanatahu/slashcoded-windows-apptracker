# Changelog

## 0.3.0 - 2026-05-05

- Switched desktop uploads to the flat `contractVersion = "v3"` app event contract.
- Removed redundant nested payload fields from uploads, including process aliases, process path, segment timestamps, and deterministic event id.
- Kept process identity, display name, timezone metadata, duration, and tracker timing diagnostics as top-level event fields.

## 0.2.0 - 2026-04-14

- Added shared host timing integration via `/api/host/handshake` and `/api/host/tracking-config`.
- Enforced shared segment duration and idle cutoff in desktop foreground app event emission.
- Added diagnostic payload metadata for timing parity checks: `trackerConfigVersion`, `segmentDurationSeconds`, and `idleThresholdSeconds`.
- Added automated tests for host config fallback, event payload timing, idle cutoff, segment boundaries, and config refresh.
