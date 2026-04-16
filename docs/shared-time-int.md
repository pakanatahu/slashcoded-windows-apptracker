# Windows Tracker Shared Timing Integration Spec

Status: Ready for implementation  
Date: 2026-04-14  
Audience: Agents implementing the `slashcoded-windows-tracker` repo

## Purpose
Make the standalone Windows tracker use the same segment duration and idle cutoff as every other Slashcoded producer.

This is the host-focus producer. Its timing contract must be the baseline that browser extension, IDE extension, and MultiTerminal align to.

This spec extends, but does not replace:
- [windows-tracker-v2-integration-spec.md](/C:/github/Coding-Tracker-Server/docs/windows-tracker-v2-integration-spec.md)

## Required timing contract
The tracker must fetch and use:
- `GET /api/host/handshake`
- `GET /api/host/tracking-config`

Current contract shape:
```json
{
  "segmentDurationSeconds": 15,
  "idleThresholdSeconds": 300,
  "configVersion": "2026-04-14T00:00:00.0000000Z",
  "updatedAt": "2026-04-14T00:00:00.0000000Z"
}
```

Default values:
- `segmentDurationSeconds = 15`
- `idleThresholdSeconds = 300`

The tracker must not keep divergent hardcoded runtime timing once host config is available.

## Startup and refresh flow
Required boot order:
1. Discover Local API with `GET /api/host/handshake`
2. Fetch `GET /api/host/tracking-config`
3. Cache the config in memory
4. Start foreground app tracking using that config
5. Refresh config every 5 minutes

Failure policy:
- keep the last known good config if refresh fails
- only use `15s / 300s` as startup fallback before first successful fetch

## Required tracker timing semantics
Shared rules:
- segment max length must equal `segmentDurationSeconds`
- `durationMs` must never exceed `segmentDurationSeconds * 1000`
- `segment_end_ts - segment_start_ts` must equal `durationMs`
- `occurredAt` should equal segment end time in UTC

Foreground tracking rules:
- only the active foreground app should emit a slice
- if the foreground process changes, close the current segment and open a new one
- if the window title changes in a way that changes event identity, close the current segment and open a new one
- do not emit long backfilled slices after wake, suspension, or delayed flush

Idle rules:
- if user activity is absent for `idleThresholdSeconds`, stop emitting focused app slices
- user return after idle must start a new segment, not extend the pre-idle segment

## Required upload metadata
Every emitted event should include:
```json
{
  "trackerConfigVersion": "2026-04-14T00:00:00.0000000Z",
  "segmentDurationSeconds": 15,
  "idleThresholdSeconds": 300
}
```

Placement:
- include these fields in `payload`

These fields are for diagnostics and parity checks. They are not trust credentials.

## Required event example
```json
{
  "contractVersion": "v2",
  "events": [
    {
      "source": "desktop",
      "occurredAt": "2026-04-14T09:15:30.000Z",
      "durationMs": 15000,
      "category": "app",
      "payload": {
        "type": "app",
        "event_id": "desktop-1713086130000-chrome",
        "process": "chrome.exe",
        "processName": "chrome.exe",
        "processPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
        "displayName": "Google Chrome",
        "windowTitle": "Pull requests - Google Chrome",
        "segment_start_ts": 1713086115000,
        "segment_end_ts": 1713086130000,
        "trackerConfigVersion": "2026-04-14T00:00:00.0000000Z",
        "segmentDurationSeconds": 15,
        "idleThresholdSeconds": 300
      }
    }
  ]
}
```

## Implementation requirements
- Replace any local polling/flush interval assumptions that differ from host timing
- Use `segmentDurationSeconds` as the max slice size for foreground app events
- Use `idleThresholdSeconds` as the AFK cutoff
- Reuse the same `event_id` on retry for the same closed segment
- Keep producer semantics neutral: `category = "app"`
- Preserve process/window facts exactly as before

## Acceptance criteria
- Foreground app slices default to 15-second max segments
- AFK cutoff defaults to 5 minutes
- Payload metadata proves which timing config the tracker used
- Desktop slices line up closely with richer child-producer slices so overlap replacement is predictable

## Explicit non-goals
- The tracker should not classify productivity
- The tracker should not assign projects directly
- The tracker should not try to decide aggregation precedence
