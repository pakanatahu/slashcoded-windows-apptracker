# Observer Policy Discovery Flow Implementation Plan

Date: 2026-06-04
Repo: slashcoded-windows-apptracker

## Goal

Make the Windows App Observer honor Local API observer policy flags from `/api/desktop/apps/policy`:

- `measureEnabled`
- `discoveryEnabled`

## Files Likely In Scope

- `DesktopAppPolicy.cs`
- `Worker.cs`
- `tests/Slashcoded.DesktopObserver.Tests/WorkerTimingTests.cs`
- `README.md`
- `CHANGELOG.md`

## Task 1: Extend Desktop Policy Model

Update `DesktopAppPolicy` and the DTO used by `Worker.RefreshPolicyAsync` to carry policy flags and separate app lists:

```csharp
MeasureEnabled
DiscoveryEnabled
```

Expected defaults for old API responses:

```csharp
MeasureEnabled = true
DiscoveryEnabled = true
```

If an old mixed `apps` list is present, derive:

```csharp
AllowedApps = apps.Where(app => app.IsAllowed && !app.IsIgnored)
IgnoredApps = apps.Where(app => app.IsIgnored)
```

Primary new contract semantics:

- `allowedApps` means measure/upload.
- `ignoredApps` means do not measure/upload and do not discover.
- absent from both means unknown, eligible for discovery.

Add unit coverage for parsing:

- New policy response with both flags false and separate app lists.
- Legacy policy response with no flags.
- Legacy mixed app rows map into `AllowedApps` and `IgnoredApps`.

## Task 2: Gate Measurement

In `Worker`, before starting or continuing foreground measurement, check current policy:

```csharp
if (!_policy.MeasureEnabled) { ... }
```

Required behavior:

- Do not build upload payloads while disabled.
- Do not enqueue measured segments while disabled.
- If a segment is currently active when policy changes to disabled, discard it without upload.
- Continue policy polling.

Add tests:

- `measureEnabled=false` produces no upload for an otherwise allowed app.
- Switching from enabled to disabled discards the active segment.
- Switching back to enabled allows future allowed-app uploads.

## Task 3: Gate Discovery

In `Worker.ReportDiscoveryAsync` or the caller around it, check:

```csharp
if (!_policy.DiscoveryEnabled || !_policy.MeasureEnabled) return;
```

Rationale: foreground observation is the mechanism that detects unknown apps. When measurement is off, avoid storing or reporting activity-like state.

Discovery must stay on the first-observation path. When discovery is enabled and the current foreground app is absent from both `allowedApps` and `ignoredApps`, the observer must immediately POST to `/api/desktop/apps/discover` during that tick. It must not wait for the next segment upload slice, and an unknown app must still produce no `/api/upload` activity until policy marks it allowed.

Add tests:

- Unknown app with `discoveryEnabled=false` does not POST `/api/desktop/apps/discover`.
- Unknown app with `measureEnabled=false` does not POST discovery.
- Unknown app with both flags true does POST discovery.
- Unknown app with discovery enabled POSTs discovery on first observation before any upload slice is emitted.

If existing test infrastructure cannot inspect discovery POSTs cleanly, introduce a small abstraction for policy/discovery HTTP behavior rather than asserting through logs.

## Task 4: Preserve Allowed Upload Behavior

Confirm policy combination and separate app-list behavior:

```json
{ "measureEnabled": true, "discoveryEnabled": false }
```

Expected:

- Apps in `allowedApps` still upload.
- Apps in `ignoredApps` do not upload and are not discovered.
- Unknown apps are not discovered when discovery is disabled.

Add focused test coverage.

## Task 5: Docs

Update README desktop policy section to show:

```json
{
  "configVersion": "...",
  "measureEnabled": true,
  "discoveryEnabled": true,
  "allowedApps": [],
  "ignoredApps": []
}
```

Document:

- `measureEnabled` disables measurement and uploads.
- `discoveryEnabled` disables unknown-app discovery.
- `allowedApps` and `ignoredApps` replace mixed `isAllowed` and `isIgnored` flags in the external policy contract.

Update CHANGELOG with a concise unreleased entry.

## Verification

Recommended commands:

```powershell
dotnet test C:\github\slashcoded-windows-apptracker\tests\Slashcoded.DesktopObserver.Tests\Slashcoded.DesktopObserver.Tests.csproj
```

Manual smoke test:

1. Start Local API.
2. Set Windows App Observer measurement off from Home Observer control.
3. Confirm observer keeps polling policy but uploads no app activity.
4. Set measurement on and discovery off.
5. Confirm allowed apps upload and new apps do not appear in discovery.
6. Set discovery on.
7. Confirm unknown apps appear for allowlisting.

