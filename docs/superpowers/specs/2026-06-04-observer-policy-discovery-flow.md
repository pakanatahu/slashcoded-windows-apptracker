# Observer Policy Discovery Flow Spec

Date: 2026-06-04
Repo: slashcoded-windows-apptracker

## Context

SlashCoded Local API now separates observer measurement from discovery. The Windows App Observer must read its external policy from the Local API before measuring, uploading, or discovering apps.

The relevant Local API endpoint is:

```http
GET {ApiBaseUrl}/api/desktop/apps/policy
```

The response now includes top-level observer control flags:

```json
{
  "configVersion": "2026-06-04T12:00:00.0000000Z",
  "measureEnabled": true,
  "discoveryEnabled": true,
  "allowedApps": [
    {
      "processName": "code",
      "displayName": "Visual Studio Code"
    }
  ],
  "ignoredApps": [
    {
      "processName": "steam",
      "displayName": "Steam"
    }
  ]
}
```

Existing upload endpoint remains:

```http
POST {ApiBaseUrl}/api/upload
```

Existing discovery endpoint remains:

```http
POST {ApiBaseUrl}/api/desktop/apps/discover
```

## Required Behavior

### Measurement policy

`measureEnabled` controls all measured activity behavior.

When `measureEnabled` is `true`:

- The observer may measure foreground app time.
- The observer may upload measured activity for apps in `allowedApps`.
- Apps in `ignoredApps` should not be measured, uploaded, or rediscovered.

When `measureEnabled` is `false`:

- The observer must not measure foreground activity.
- The observer must not queue upload events.
- The observer must not upload already accumulated measurement events from disabled time.
- The observer should keep polling `/api/desktop/apps/policy` so it can resume when `measureEnabled` becomes `true`.
- Any active in-progress measurement segment should be closed or discarded without upload when the disabled policy is observed.

### Discovery policy

`discoveryEnabled` controls new app discovery only.

When `discoveryEnabled` is `true`:

- The observer may call `/api/desktop/apps/discover` for unknown apps.
- Discovery should remain near-instant enough that users can open an app and see it appear in SlashCoded for allowlisting.

When `discoveryEnabled` is `false`:

- The observer must not call `/api/desktop/apps/discover`.
- Measurement for already allowed apps may continue if `measureEnabled` is `true`.
- Policy polling must continue.

### Interaction matrix

| measureEnabled | discoveryEnabled | Expected observer behavior |
| --- | --- | --- |
| true | true | Measure and upload allowed apps; discover unknown apps. |
| true | false | Measure and upload allowed apps; do not discover unknown apps. |
| false | true | Do not measure or upload; may discover unknown apps if the implementation can do so without storing measured activity. |
| false | false | Do not measure, upload, or discover; only poll policy. |

For the Windows App Observer, discovery depends on foreground app observation. If keeping discovery active while measurement is disabled would require recording measurement state, prefer no discovery while `measureEnabled` is false. The important invariant is: `measureEnabled: false` must not create measured activity locally or remotely.

## Policy Refresh

The observer should refresh `/api/desktop/apps/policy` on its existing allowlist/policy cadence and when foreground app state changes if the cached policy is stale.

Recommended handling:

- Cache the latest policy in memory.
- Keep using the current policy TTL/cooldown pattern.
- On policy fetch failure, keep the last known policy briefly.
- If no policy has ever been loaded, default to safe behavior: `measureEnabled = false`, `discoveryEnabled = false`, no allowed apps, and no ignored apps.

## Data Model

Replace the mixed app row contract with separate policy lists. The existing desktop policy model should include:

```csharp
public bool MeasureEnabled { get; init; }
public bool DiscoveryEnabled { get; init; }
public IReadOnlyList<DesktopAppPolicyEntry> AllowedApps { get; init; }
public IReadOnlyList<DesktopAppPolicyEntry> IgnoredApps { get; init; }
```

Contract shape:

- `allowedApps` means measure and upload this process.
- `ignoredApps` means do not measure, upload, or report this process as newly discovered.
- A process absent from both lists is unknown and eligible for discovery when discovery is enabled.

Default handling for older Local API responses:

- Missing `measureEnabled` should be treated as `true` for compatibility.
- Missing `discoveryEnabled` should be treated as `true` for compatibility.
- If the old mixed `apps` list is present, derive `allowedApps` from rows where `isAllowed && !isIgnored`, and derive `ignoredApps` from rows where `isIgnored`.

## Observability

Log policy changes at info level when either flag changes:

- `measureEnabled`
- `discoveryEnabled`

Avoid noisy logs on every poll when values are unchanged.

## Acceptance Criteria

- The observer fetches and parses `measureEnabled`, `discoveryEnabled`, `allowedApps`, and `ignoredApps` from `/api/desktop/apps/policy`.
- `measureEnabled: false` prevents measuring, local event accumulation, and uploads.
- `discoveryEnabled: false` prevents `/api/desktop/apps/discover` calls.
- Allowed app uploads continue when `measureEnabled: true` and `discoveryEnabled: false`.
- Policy polling continues while measurement and/or discovery are disabled.
- Existing older policy responses without the new flags remain compatible.
- The observer no longer depends on mixed `isAllowed` and `isIgnored` flags in the primary policy contract.
- Unit tests cover all policy flag combinations that affect measurement and discovery.

