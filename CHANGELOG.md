# Changelog

## 0.2.0 - 2026-04-14

- Added shared host timing integration via `/api/host/handshake` and `/api/host/tracking-config`.
- Enforced shared segment duration and idle cutoff in desktop foreground app event emission.
- Added diagnostic payload metadata for timing parity checks: `trackerConfigVersion`, `segmentDurationSeconds`, and `idleThresholdSeconds`.
- Added automated tests for host config fallback, event payload timing, idle cutoff, segment boundaries, and config refresh.

