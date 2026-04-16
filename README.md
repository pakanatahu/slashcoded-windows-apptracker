# Slashcoded.DesktopTracker

Windows desktop activity tracker used by Slashcoded Desktop. It measures foreground app focus on Windows and uploads neutral host-surface `app` events to the local Slashcoded API `v2` ingestion contract.

## What it does

- Polls the active window/process at a regular interval.
- Resolves the app/process into a stable name.
- Fetches shared host timing from the local API before foreground tracking starts.
- Segments the current foreground app using the host `segmentDurationSeconds` value.
- Stops emitting focused app slices when idle time reaches the host `idleThresholdSeconds` value.
- Emits neutral `contractVersion = "v2"` desktop `app` events and sends them to the local API for normalization into `activity_events_v2`.
- Polls the local API allowlist on a regular cadence only to suppress duplicate discovery reports for known apps.
- Registers a trusted source once and signs each upload with trust headers.

## Configuration

Set these under `Tracker` in `appsettings.json`:

- `ApiBaseUrl`: Base URL for the local API.
- `HeartbeatIntervalSeconds`: Polling interval for foreground window checks.
- `SleepGapThresholdMinutes`: Gap duration that triggers a reset of tracking state.

Segment duration and idle cutoff are not local tracker settings. The tracker discovers them from:

- `GET {ApiBaseUrl}/api/host/handshake`
- `GET {ApiBaseUrl}/api/host/tracking-config`

Startup order:

1. Discover the local API with `/api/host/handshake`.
2. Fetch `/api/host/tracking-config`.
3. Cache the config in memory.
4. Start foreground app tracking with that config.
5. Refresh the config every 5 minutes.

If startup or refresh fails, the tracker keeps the last known good config. Before the first successful fetch, it uses the shared defaults: `segmentDurationSeconds = 15` and `idleThresholdSeconds = 300`.

## How Slashcoded uses it

Slashcoded Desktop runs this tracker as a background process. The local API receives events and writes them into the local database. The analytics pipeline then includes desktop activity in heatmaps, summaries, and reports.

## How to integrate it in your own app

1. Run the tracker as a background process or service on Windows.
2. Provide a local HTTP endpoint that accepts activity events.
3. Map incoming events into your own storage or analytics pipeline.

## Integration guide

The tracker posts JSON to the local API at `POST {ApiBaseUrl}/api/upload`.

### Shared timing contract

The local API must expose `GET {ApiBaseUrl}/api/host/tracking-config` and return JSON like:

```json
{
  "segmentDurationSeconds": 15,
  "idleThresholdSeconds": 300,
  "configVersion": "2026-04-14T00:00:00.0000000Z",
  "updatedAt": "2026-04-14T00:00:00.0000000Z"
}
```

Foreground app event rules:

- A segment never exceeds `segmentDurationSeconds`.
- `durationMs` equals `payload.segment_end_ts - payload.segment_start_ts`.
- `occurredAt` is the segment end time in UTC.
- Process or window-title identity changes close the current segment and start a new one.
- Idle cutoff closes the current segment and suppresses further focused slices until activity returns.
- User return after idle starts a new segment instead of extending the pre-idle segment.

### Trusted source enrollment

Before signed uploads, the tracker performs one-time enrollment with:

`POST {ApiBaseUrl}/api/security/sources/register`

Request body:

```json
{
  "clientId": "desktop-tracker",
  "clientType": "desktop",
  "machineId": "<stable-machine-id>",
  "displayName": "Windows App Tracker"
}
```

The tracker persists the returned `sourceId` and `secret` in user-local app data using Windows DPAPI.

### Trusted upload headers

Each upload request includes:

- `X-Sc-Source-Id`
- `X-Sc-Timestamp` (unix-ms)
- `X-Sc-Nonce` (new per request)
- `X-Sc-Signature` (HMAC-SHA256 base64)

Signature base string:

```text
METHOD + "\n" + PATH + "\n" + TIMESTAMP + "\n" + NONCE + "\n" + SHA256_BASE64(rawBodyBytes)
```

The tracker signs exact outbound body bytes and regenerates timestamp/nonce/signature on retry.

### Allowlist and discovery contract

The tracker expects the local API to expose `GET {ApiBaseUrl}/api/desktop/apps/allowlist` and return JSON like:

```json
{
  "apps": [
    {
      "processName": "explorer",
      "displayName": "Windows Explorer",
      "category": "good"
    }
  ]
}
```

Known apps in the allowlist are not re-reported to the discovery endpoint. The allowlist no longer controls whether focus events are uploaded.

Unknown apps can still be reported to:

`POST {ApiBaseUrl}/api/desktop/apps/discover`

### Data contract

The request body is an object with `contractVersion = "v2"` and an `events` array. Each event uses the following shape:

```json
{
  "contractVersion": "v2",
  "events": [
    {
      "source": "desktop",
      "occurredAt": "2026-03-14T08:31:30.0000000Z",
      "durationMs": 15000,
      "project": null,
      "category": "app",
      "payload": {
        "type": "app",
        "event_id": "desktop-9f7d0f4e1710405075000",
        "process": "explorer.exe",
        "processName": "explorer.exe",
        "processPath": "C:\\\\WINDOWS\\\\Explorer.EXE",
        "displayName": "Windows Explorer",
        "windowTitle": "",
        "segment_start_ts": 1710405075000,
        "segment_end_ts": 1710405090000,
        "trackerConfigVersion": "2026-04-14T00:00:00.0000000Z",
        "segmentDurationSeconds": 15,
        "idleThresholdSeconds": 300
      }
    }
  ]
}
```

Notes:

- `occurredAt` is UTC ISO 8601 and must match the segment end time.
- `durationMs` must equal `payload.segment_end_ts - payload.segment_start_ts`.
- `category` is always neutral `"app"`.
- `payload.event_id` is deterministic for a given segment and reused on retry.
- `payload.process` and `payload.processName` are normalized executable names such as `chrome.exe`.
- `payload.processPath` and `payload.displayName` are recommended metadata when available.
- `payload.trackerConfigVersion`, `payload.segmentDurationSeconds`, and `payload.idleThresholdSeconds` are diagnostic metadata showing the timing config used to emit the slice.
- Upload payloads above `16KB` are rejected client-side.

The tracker is designed to be source-agnostic. If your app exposes an HTTP API that accepts JSON activity events, you can reuse the tracker to capture Windows app usage and feed your system.

## Notes

- It is Windows-only.
- Data stays on the local machine unless your API forwards it elsewhere.
- This service is not standalone; without a local API that accepts uploads, it will not record useful activity. Discovery deduplication also expects the allowlist endpoint.
- See `CHANGELOG.md` and `docs/releases/` for release notes.
