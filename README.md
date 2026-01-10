# Slashcoded.DesktopTracker

Windows desktop activity tracker used by Slashcoded Desktop. It measures foreground app usage on Windows, normalizes it into "desktop activity" events, and uploads them to the local Slashcoded API so the SQLite DB and reports stay in sync.

## What it does

- Polls the active window/process at a regular interval.
- Resolves the app/process into a stable name and category.
- Emits events in the same shape as other Slashcoded sources.
- Sends those events to the local API so they are stored and aggregated.

## Configuration

Set these under `Tracker` in `appsettings.json`:

- `ApiBaseUrl`: Base URL for the local API.
- `UserId`: User identifier sent with events.
- `HeartbeatIntervalSeconds`: Polling interval for foreground window checks.
- `FlushIntervalMinutes`: Upload chunk size for continuous activity.
- `SleepGapThresholdMinutes`: Gap duration that triggers a reset of tracking state.

## How Slashcoded uses it

Slashcoded Desktop runs this tracker as a background process. The local API receives events and writes them into the local database. The analytics pipeline then includes desktop activity in heatmaps, summaries, and reports.

## How to integrate it in your own app

1. Run the tracker as a background process or service on Windows.
2. Provide a local HTTP endpoint that accepts activity events.
3. Map incoming events into your own storage or analytics pipeline.

## Integration guide

The tracker posts JSON to the local API at `POST {ApiBaseUrl}/api/upload`.

### Data contract

The request body is an object with an `events` array. Each event uses the following shape:

```json
{
  "events": [
    {
      "userId": "local",
      "source": "desktop",
      "occurredAt": "2026-01-10T22:22:01.7304875+07:00",
      "durationMs": 60000,
      "processName": "explorer",
      "payload": {
        "processPath": "C:\\\\WINDOWS\\\\Explorer.EXE",
        "windowTitle": "",
        "duration_ms": 60000
      }
    }
  ]
}
```

Notes:

- `occurredAt` uses ISO 8601 format.
- `durationMs` and `payload.duration_ms` are in milliseconds (rounded from seconds).
- `processName` is the process name, and `payload.processPath` is the full path when available.

The tracker is designed to be source-agnostic. If your app exposes an HTTP API that accepts JSON activity events, you can reuse the tracker to capture Windows app usage and feed your system.

## Notes

- It is Windows-only.
- Data stays on the local machine unless your API forwards it elsewhere.
