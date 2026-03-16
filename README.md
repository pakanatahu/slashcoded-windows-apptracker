# Slashcoded.DesktopTracker

Windows desktop activity tracker used by Slashcoded Desktop. It measures foreground app focus on Windows and uploads neutral host-surface `app` events to the local Slashcoded API `v2` ingestion contract.

## What it does

- Polls the active window/process at a regular interval.
- Resolves the app/process into a stable name.
- Segments the current foreground app into 5-30 second focus slices.
- Emits neutral `contractVersion = "v2"` desktop `app` events and sends them to the local API for normalization into `activity_events_v2`.
- Polls the local API allowlist on a regular cadence only to suppress duplicate discovery reports for known apps.
- Registers a trusted source once and signs each upload with trust headers.

## Configuration

Set these under `Tracker` in `appsettings.json`:

- `ApiBaseUrl`: Base URL for the local API.
- `HeartbeatIntervalSeconds`: Polling interval for foreground window checks.
- `FlushIntervalSeconds`: Preferred upload chunk size for continuous activity. The tracker clamps this to the supported 5-30 second range.
- `FlushIntervalMinutes`: Legacy fallback if `FlushIntervalSeconds` is unset. Legacy values are converted into the same 5-30 second range.
- `SleepGapThresholdMinutes`: Gap duration that triggers a reset of tracking state.

## How Slashcoded uses it

Slashcoded Desktop runs this tracker as a background process. The local API receives events and writes them into the local database. The analytics pipeline then includes desktop activity in heatmaps, summaries, and reports.

## How to integrate it in your own app

1. Run the tracker as a background process or service on Windows.
2. Provide a local HTTP endpoint that accepts activity events.
3. Map incoming events into your own storage or analytics pipeline.

## Integration guide

The tracker posts JSON to the local API at `POST {ApiBaseUrl}/api/upload`.

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
        "segment_end_ts": 1710405090000
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
- Upload payloads above `16KB` are rejected client-side.

The tracker is designed to be source-agnostic. If your app exposes an HTTP API that accepts JSON activity events, you can reuse the tracker to capture Windows app usage and feed your system.

## Notes

- It is Windows-only.
- Data stays on the local machine unless your API forwards it elsewhere.
- This service is not standalone; without a local API that accepts uploads, it will not record useful activity. Discovery deduplication also expects the allowlist endpoint.
