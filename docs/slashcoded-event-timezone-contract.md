# SlashCoded Event Timezone Contract

## Purpose

SlashCoded event producers must include timezone metadata with each uploaded event. Local API stores UTC timestamps as authoritative event time, and it stores timezone metadata as the local context where the event happened.

Report timezone remains the default timezone for SlashCoded charts and exports. Event timezone metadata supports historical backfill, timezone detection, and future timezone-grouped analytics.

## Canonical Fields

Every producer should send these top-level event fields when uploading to Local API:

```json
{
  "occurredAt": "2026-05-03T09:15:00.000Z",
  "durationMs": 60000,
  "timezone": "Asia/Bangkok",
  "timezoneOffsetMinutes": 420,
  "timezoneSource": "producer"
}
```

Field rules:

- `timezone` is the IANA timezone ID for the user's local timezone when the event occurred.
- `timezoneOffsetMinutes` is the UTC offset at the event timestamp, east-positive.
- `timezoneSource` should be `producer` for values captured directly by this producer.
- `occurredAt` remains UTC-authoritative for ordering and dedupe.
- Producers should not put timezone only inside nested payload JSON when they can send top-level fields.

Valid timezone examples:

- `Asia/Bangkok`
- `Europe/Copenhagen`
- `America/New_York`
- `UTC`

## JavaScript Producers

Browser, VSCode, and Node/Electron-style producers should use the platform `Intl` API:

```ts
const now = new Date();
const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
const timezoneOffsetMinutes = -now.getTimezoneOffset();
```

Attach the values to every event:

```ts
{
  occurredAt: now.toISOString(),
  durationMs,
  timezone,
  timezoneOffsetMinutes,
  timezoneSource: 'producer'
}
```

The offset must be calculated for the same timestamp represented by the event, not once at process startup.

## Windows .NET Producer

The Windows tracker should report both canonical IANA data and Windows compatibility data when possible.

Recommended fields:

```json
{
  "timezone": "Europe/Copenhagen",
  "timezoneOffsetMinutes": 120,
  "timezoneSource": "producer",
  "windowsTimezone": "Romance Standard Time"
}
```

Implementation guidance:

- Read the Windows timezone with `TimeZoneInfo.Local.Id` and send it as `windowsTimezone`.
- Calculate `timezoneOffsetMinutes` with `TimeZoneInfo.Local.GetUtcOffset(eventTime).TotalMinutes`.
- Convert the Windows timezone ID to IANA when possible and send that as `timezone`.
- If conversion fails, still send `windowsTimezone`, `timezoneOffsetMinutes`, and `timezoneSource`.

## Validation Requirements

Producer tests should verify:

- Every uploaded event includes `timezoneOffsetMinutes` and `timezoneSource`.
- JS producers include an IANA `timezone` from `Intl.DateTimeFormat().resolvedOptions().timeZone`.
- The Windows tracker includes `windowsTimezone` and includes canonical `timezone` when conversion succeeds.
- `timezoneOffsetMinutes` is east-positive. For example, Bangkok is `420`, Copenhagen winter is `60`, and UTC is `0`.
- Timezone values are top-level event fields, not only nested payload fields.

## Backward Compatibility

Local API will accept events without timezone metadata during migration, but producers should start sending these fields as soon as they adopt this contract.
