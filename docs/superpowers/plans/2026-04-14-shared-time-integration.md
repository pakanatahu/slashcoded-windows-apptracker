# Shared Timing Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows tracker fetch shared timing config from the host API, enforce the shared segment and idle contract at runtime, include config metadata in uploads, and ship the change with tests, version bumps, and release documentation.

**Architecture:** Replace tracker-owned timing decisions with a small host-config layer that discovers the local API, loads `/api/host/tracking-config`, stores the last known good config in memory, and refreshes it every 5 minutes. Refactor `Worker` so segment emission depends on the active runtime config instead of `FlushInterval*`, and add deterministic unit tests around config fallback, idle transitions, segment boundaries, and payload metadata before updating public docs and release artifacts.

**Tech Stack:** .NET 8 Worker Service, `HttpClient`, `Microsoft.Extensions.Options`, xUnit test project, solution-level `dotnet test` verification, Markdown release docs.

---

## Planned File Map

**Existing files to modify**
- `Program.cs`
  Registers any new host-config services and testable abstractions needed by `Worker`.
- `Worker.cs`
  Moves timing logic from local option-based intervals to shared host config, idle cutoff behavior, refresh cadence, and payload metadata.
- `TrackerOptions.cs`
  Removes or deprecates local timing knobs that must no longer diverge after host config loads.
- `appsettings.json`
  Keeps only supported tracker configuration and any refresh/discovery settings that remain configurable.
- `Slashcoded.DesktopTracker.csproj`
  Bumps app version metadata and adds any test-friendly assembly settings if needed.
- `slashcoded-windows-apptracker.sln`
  Adds the new test project.
- `README.md`
  Updates configuration, runtime behavior, payload contract, and release notes references.
- `CONTRIBUTING.md`
  Updates bug-report guidance to mention shared timing config and tracker version expectations.

**New source files to create**
- `HostTrackingConfig.cs`
  Strongly typed runtime timing contract with defaults (`15s`, `300s`) and host metadata.
- `HostHandshakeResponse.cs`
  DTO for `/api/host/handshake` response if discovery data is structured.
- `HostTrackingConfigResponse.cs`
  DTO for `/api/host/tracking-config`.
- `IHostTrackingConfigProvider.cs`
  Interface exposing current config, startup initialization, and refresh behavior.
- `HostTrackingConfigProvider.cs`
  Fetches handshake + tracking config, caches the last known good config, and refreshes every 5 minutes.
- `ISystemClock.cs`
  Small abstraction for deterministic timing tests.
- `SystemClock.cs`
  Production clock implementation.
- `IIdleMonitor.cs`
  Interface for reading last user-input time / AFK state without hard-coding OS calls inside `Worker`.
- `WindowsIdleMonitor.cs`
  Windows implementation used in production.
- `TrackingEventBuilder.cs`
  Builds upload payloads with deterministic `event_id` reuse and shared timing metadata.

**New test files to create**
- `tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj`
- `tests/Slashcoded.DesktopTracker.Tests/HostTrackingConfigProviderTests.cs`
- `tests/Slashcoded.DesktopTracker.Tests/TrackingEventBuilderTests.cs`
- `tests/Slashcoded.DesktopTracker.Tests/WorkerTimingTests.cs`
- `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeClock.cs`
- `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeIdleMonitor.cs`
- `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeHostTrackingConfigProvider.cs`
- `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeActiveWindowMonitor.cs`
- `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeTrustedUploadClient.cs`

**New release-doc files to create**
- `CHANGELOG.md`
  Repo-level changelog starting with this feature release if no changelog already exists.
- `docs/releases/2026-04-14-shared-time-integration.md`
  Short operator-facing release note with behavior changes, migration notes, and verification checklist.

### Task 1: Create Test Harness and Runtime Abstractions

**Files:**
- Create: `tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj`
- Create: `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeClock.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeIdleMonitor.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeHostTrackingConfigProvider.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeActiveWindowMonitor.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/Fakes/FakeTrustedUploadClient.cs`
- Create: `ISystemClock.cs`
- Create: `SystemClock.cs`
- Create: `IIdleMonitor.cs`
- Create: `WindowsIdleMonitor.cs`
- Modify: `Program.cs`
- Modify: `slashcoded-windows-apptracker.sln`

- [x] **Step 1: Create the xUnit test project**

Run: `dotnet new xunit -n Slashcoded.DesktopTracker.Tests -o tests/Slashcoded.DesktopTracker.Tests`
Expected: project scaffolded under `tests/Slashcoded.DesktopTracker.Tests`

- [x] **Step 2: Add the test project to the solution**

Run: `dotnet sln slashcoded-windows-apptracker.sln add tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj`
Expected: solution updated with one test project

- [x] **Step 3: Add a project reference to the tracker app**

Modify `tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj` so it references the main project:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Slashcoded.DesktopTracker.csproj" />
</ItemGroup>
```

- [x] **Step 4: Introduce clock and idle-monitor interfaces before touching `Worker`**

Add interfaces with minimal surface area:

```csharp
public interface ISystemClock
{
    DateTimeOffset Now { get; }
}

public interface IIdleMonitor
{
    TimeSpan GetIdleDuration();
}
```

- [x] **Step 5: Register production implementations in DI**

Update `Program.cs` to register:

```csharp
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<IIdleMonitor, WindowsIdleMonitor>();
```

- [ ] **Step 6: Commit**

Execution note: commit deferred until the implementation is complete to avoid sweeping the existing untracked spec doc into an intermediate commit.

Run:
```bash
git add Program.cs slashcoded-windows-apptracker.sln ISystemClock.cs SystemClock.cs IIdleMonitor.cs WindowsIdleMonitor.cs tests/Slashcoded.DesktopTracker.Tests
git commit -m "test: add timing integration test harness"
```

### Task 2: Add Host Tracking Config Provider with Startup Fallback and Refresh

**Files:**
- Create: `HostTrackingConfig.cs`
- Create: `HostHandshakeResponse.cs`
- Create: `HostTrackingConfigResponse.cs`
- Create: `IHostTrackingConfigProvider.cs`
- Create: `HostTrackingConfigProvider.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/HostTrackingConfigProviderTests.cs`
- Modify: `Program.cs`
- Modify: `TrackerOptions.cs`
- Modify: `appsettings.json`

- [x] **Step 1: Write failing tests for startup fallback, successful load, and failed refresh retention**

Create tests like:

```csharp
[Fact]
public async Task InitializeAsync_UsesDefaultsBeforeFirstSuccessfulFetch()
{
    var provider = CreateProvider(handshakeStatus: HttpStatusCode.InternalServerError);

    await provider.InitializeAsync(CancellationToken.None);

    provider.Current.SegmentDurationSeconds.Should().Be(15);
    provider.Current.IdleThresholdSeconds.Should().Be(300);
}
```

```csharp
[Fact]
public async Task RefreshAsync_KeepsLastKnownGoodConfig_WhenRefreshFails()
{
    var provider = CreateProvider(successConfig: new(20, 420, "v1"), refreshFailure: true);

    await provider.InitializeAsync(CancellationToken.None);
    await provider.RefreshAsync(CancellationToken.None);

    provider.Current.ConfigVersion.Should().Be("v1");
}
```

- [x] **Step 2: Run the provider tests and verify they fail**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter HostTrackingConfigProviderTests`
Expected: FAIL because provider types do not exist yet

- [x] **Step 3: Implement the config contract and provider**

Shape the runtime config as:

```csharp
public sealed record HostTrackingConfig(
    int SegmentDurationSeconds,
    int IdleThresholdSeconds,
    string? ConfigVersion,
    DateTimeOffset? UpdatedAt)
{
    public static HostTrackingConfig Default { get; } = new(15, 300, null, null);
}
```

Provider requirements:
- call `GET /api/host/handshake` before `GET /api/host/tracking-config`
- expose `Current`
- start with `HostTrackingConfig.Default`
- replace config only on successful fetch
- keep last known good config on refresh failure
- expose a `RefreshInterval = TimeSpan.FromMinutes(5)`

- [x] **Step 4: Remove runtime ownership of local timing knobs from `TrackerOptions`**

Execution note: local flush properties were temporarily retained while the old worker compiled, then removed after the Task 4 worker refactor.

Keep only settings still owned by the tracker, for example:

```csharp
public sealed class TrackerOptions
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5292";
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public int SleepGapThresholdMinutes { get; set; } = 5;
}
```

Document in comments or README that segment and idle timing now come from the host API, not local config.

- [x] **Step 5: Register the provider in DI**

Update `Program.cs`:

```csharp
builder.Services.AddSingleton<IHostTrackingConfigProvider, HostTrackingConfigProvider>();
```

- [x] **Step 6: Run tests and make them pass**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter HostTrackingConfigProviderTests`
Expected: PASS

- [ ] **Step 7: Commit**

Execution note: commit deferred until the implementation is complete.

Run:
```bash
git add Program.cs TrackerOptions.cs appsettings.json HostTrackingConfig.cs HostHandshakeResponse.cs HostTrackingConfigResponse.cs IHostTrackingConfigProvider.cs HostTrackingConfigProvider.cs tests/Slashcoded.DesktopTracker.Tests/HostTrackingConfigProviderTests.cs
git commit -m "feat: add shared host tracking config provider"
```

### Task 3: Refactor Event Building to Enforce Shared Timing Metadata

**Files:**
- Create: `TrackingEventBuilder.cs`
- Create: `tests/Slashcoded.DesktopTracker.Tests/TrackingEventBuilderTests.cs`
- Modify: `Worker.cs`

- [x] **Step 1: Write failing payload-shape tests**

Cover:
- `occurredAt == segmentEnd UTC`
- `durationMs == segment_end_ts - segment_start_ts`
- `durationMs <= segmentDurationSeconds * 1000`
- payload includes `trackerConfigVersion`, `segmentDurationSeconds`, `idleThresholdSeconds`
- `event_id` is stable for the same closed segment

Example:

```csharp
[Fact]
public void Build_AppEvent_IncludesSharedTimingMetadata()
{
    var config = new HostTrackingConfig(15, 300, "2026-04-14T00:00:00.0000000Z", DateTimeOffset.Parse("2026-04-14T00:00:00Z"));
    var evt = TrackingEventBuilder.Build(sample, segmentStart, segmentEnd, config);

    evt.events[0].payload.segmentDurationSeconds.Should().Be(15);
    evt.events[0].payload.idleThresholdSeconds.Should().Be(300);
    evt.events[0].payload.trackerConfigVersion.Should().Be("2026-04-14T00:00:00.0000000Z");
}
```

- [x] **Step 2: Run the event-builder tests and verify they fail**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter TrackingEventBuilderTests`
Expected: FAIL because builder does not exist yet

- [x] **Step 3: Extract payload assembly from `Worker.PublishEventAsync` into `TrackingEventBuilder`**

Implement a builder method shaped like:

```csharp
public static object Build(
    DesktopWindowSample sample,
    DateTimeOffset segmentStart,
    DateTimeOffset segmentEnd,
    HostTrackingConfig config)
```

Builder rules:
- clamp only to `config.SegmentDurationSeconds`
- never emit non-positive durations
- preserve `process`, `processName`, `processPath`, `displayName`
- keep `category = "app"` and `contractVersion = "v2"`

- [x] **Step 4: Run builder tests and make them pass**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter TrackingEventBuilderTests`
Expected: PASS

- [ ] **Step 5: Commit**

Execution note: commit deferred until the implementation is complete.

Run:
```bash
git add Worker.cs TrackingEventBuilder.cs tests/Slashcoded.DesktopTracker.Tests/TrackingEventBuilderTests.cs
git commit -m "refactor: extract shared-timing event builder"
```

### Task 4: Refactor Worker Runtime Flow to Use Shared Config, Idle Cutoff, and 5-Minute Refresh

**Files:**
- Create: `tests/Slashcoded.DesktopTracker.Tests/WorkerTimingTests.cs`
- Modify: `Worker.cs`
- Modify: `Program.cs`

- [x] **Step 1: Write failing worker tests for timing semantics**

Add tests for:
- startup uses provider current config before first success fallback changes
- continuous focus emits slices no longer than `segmentDurationSeconds`
- process change closes the current segment and starts a new one
- idle crossing `idleThresholdSeconds` stops emission
- return from idle starts a new segment instead of extending the old segment
- suspend/resume or long loop gaps do not backfill one oversized slice
- config refresh is attempted every 5 minutes without stopping tracking

Example:

```csharp
[Fact]
public async Task IdleReturn_StartsNewSegment_InsteadOfExtendingPreIdleSegment()
{
    // arrange fake clock, fake idle monitor, and fake active-window stream
    // act by advancing time across idle threshold and then resuming activity
    // assert two closed segments with a gap, not one bridged segment
}
```

- [x] **Step 2: Run the worker timing tests and verify they fail**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter WorkerTimingTests`
Expected: FAIL because `Worker` still uses `_flushInterval` and has no idle provider/config refresh behavior

- [x] **Step 3: Refactor `Worker` constructor dependencies**

Inject:
- `IHostTrackingConfigProvider`
- `ISystemClock`
- `IIdleMonitor`
- an active-window abstraction if direct `ActiveWindowMonitor` construction blocks testing

- [x] **Step 4: Replace `_flushInterval` logic with runtime-config segment logic**

Change `FlushElapsedAsync` semantics to:
- read `var config = _hostTrackingConfigProvider.Current`
- emit while `elapsed >= config.SegmentDurationSeconds`
- never flush a partial segment on heartbeat alone
- only flush partial segments when identity changes, shutdown happens, idle starts, suspend happens, or long-gap reset happens

- [x] **Step 5: Add idle cutoff handling**

Before flushing or extending the active sample:

```csharp
if (_idleMonitor.GetIdleDuration() >= TimeSpan.FromSeconds(config.IdleThresholdSeconds))
{
    await FlushElapsedAsync(now, cancellationToken, flushAll: true);
    _activeSample = null;
    _activeStart = null;
    return;
}
```

Make sure post-idle foreground activity creates a new segment start time.

- [x] **Step 6: Add config initialization and 5-minute refresh loop**

At service startup:

```csharp
await _hostTrackingConfigProvider.InitializeAsync(stoppingToken);
```

Within the main loop:
- track the next refresh timestamp
- call `RefreshAsync` every 5 minutes
- log failures but retain `Current`

- [x] **Step 7: Re-run worker timing tests until they pass**

Run: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj --filter WorkerTimingTests`
Expected: PASS

- [x] **Step 8: Run the full test suite**

Execution note: full test project run passes in Release with 16 tests. Solution-level `dotnet test`/restore still exits during solution restore with no MSBuild error in this environment, so project-level build/test are the verified checks.

Run: `dotnet test slashcoded-windows-apptracker.sln`
Expected: PASS

- [ ] **Step 9: Commit**

Execution note: commit deferred until the implementation is complete.

Run:
```bash
git add Program.cs Worker.cs tests/Slashcoded.DesktopTracker.Tests/WorkerTimingTests.cs
git commit -m "feat: enforce shared host timing in worker runtime"
```

### Task 5: Bump Versions and Update Public Documentation

**Files:**
- Modify: `Slashcoded.DesktopTracker.csproj`
- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Create: `CHANGELOG.md`
- Create: `docs/releases/2026-04-14-shared-time-integration.md`

- [x] **Step 1: Decide and apply the release version bump**

Add explicit version properties in `Slashcoded.DesktopTracker.csproj` if missing:

```xml
<PropertyGroup>
  <Version>0.2.0</Version>
  <AssemblyVersion>0.2.0.0</AssemblyVersion>
  <FileVersion>0.2.0.0</FileVersion>
  <InformationalVersion>0.2.0</InformationalVersion>
</PropertyGroup>
```

Use the repo's actual next release number if a broader release train dictates something other than `0.2.0`.

- [x] **Step 2: Update `README.md` for the new runtime contract**

Document:
- startup boot order: handshake, tracking-config, in-memory cache, tracking start
- 5-minute refresh behavior
- startup fallback defaults (`15s`, `300s`)
- payload metadata fields
- removal of local timing ownership from `appsettings.json`

- [x] **Step 3: Update `CONTRIBUTING.md`**

Add bug-report guidance asking for:
- tracker version
- current host tracking config version
- whether issue happened before first successful config fetch or after refresh

- [x] **Step 4: Create `CHANGELOG.md`**

Add an initial entry like:

```md
# Changelog

## 0.2.0 - 2026-04-14
- Added shared host timing integration via `/api/host/handshake` and `/api/host/tracking-config`.
- Enforced shared segment duration and idle cutoff in desktop event emission.
- Added diagnostic payload metadata for tracking config parity.
```

- [x] **Step 5: Create `docs/releases/2026-04-14-shared-time-integration.md`**

Include:
- summary of behavior changes
- operator-facing validation steps
- rollback considerations
- note that timing settings now come from the host API, not local tracker config

- [ ] **Step 6: Commit**

Execution note: commit deferred until the implementation is complete.

Run:
```bash
git add Slashcoded.DesktopTracker.csproj README.md CONTRIBUTING.md CHANGELOG.md docs/releases/2026-04-14-shared-time-integration.md
git commit -m "docs: publish shared timing release notes"
```

### Task 6: Final Verification and Release Readiness Check

**Files:**
- Modify as needed: any files above if verification exposes mismatches

- [x] **Step 1: Run formatting/build verification**

Run: `dotnet build slashcoded-windows-apptracker.sln -c Release`
Expected: BUILD SUCCESSFUL / `0 Error(s)`

Execution note: `dotnet build Slashcoded.DesktopTracker.csproj -c Release --no-restore` passed with `0 Warning(s)` and `0 Error(s)`. Solution-level build exits during solution restore without a reported MSBuild error in this environment.

- [x] **Step 2: Run the full automated test suite**

Run: `dotnet test slashcoded-windows-apptracker.sln -c Release`
Expected: all tests PASS

Execution note: `dotnet test tests/Slashcoded.DesktopTracker.Tests/Slashcoded.DesktopTracker.Tests.csproj -c Release --no-restore` passed: 16 passed, 0 failed. Solution-level test exits during solution restore without a reported MSBuild error in this environment.

- [x] **Step 3: Manually verify release-facing docs mention the same defaults**

Check these files for consistent `15s` and `300s` defaults and 5-minute refresh cadence:
- `README.md`
- `CHANGELOG.md`
- `docs/releases/2026-04-14-shared-time-integration.md`
- `docs/shared-time-int.md`

- [x] **Step 4: Smoke-test config fetch behavior against a live local API**

Run the tracker against a local API exposing:
- `GET /api/host/handshake`
- `GET /api/host/tracking-config`
- `POST /api/upload`

Expected:
- first successful config fetch logged before normal tracking starts
- refresh attempts every 5 minutes
- uploaded payload contains `trackerConfigVersion`, `segmentDurationSeconds`, `idleThresholdSeconds`

Execution note: live local API endpoint checks passed for `/api/host/handshake` and `/api/host/tracking-config`. The live config returned `segmentDurationSeconds = 60` and `idleThresholdSeconds = 300`; it did not include `configVersion`, so tracker payload metadata will use `null` for `trackerConfigVersion` until the API includes that field.

- [x] **Step 5: Capture release handoff notes**

Execution note: final app version is `0.2.0`; automated Release test result is 16 passed, 0 failed; live local API config check returned segment 60s and idle 300s; solution-level restore/build/test needs follow-up because it exits without diagnostics while project-level commands pass.

Record:
- final app version
- config version used in smoke test
- test command results
- any follow-up gaps deferred from this release

- [ ] **Step 6: Commit any final verification/doc fixes**

Execution note: commit deferred because commits were not explicitly requested for this execution.

Run:
```bash
git add .
git commit -m "chore: finalize shared timing integration release"
```

## Risks and Decisions to Resolve During Execution

- The `/api/host/handshake` response shape is not defined in this repo. Confirm whether it only proves reachability or can override the API base URL before implementing the DTO.
- `Worker` currently instantiates `ActiveWindowMonitor` directly. If that blocks reliable tests, add an `IActiveWindowMonitor` abstraction early instead of fighting the current design.
- `TrustedUploadClient` is used directly. If upload verification needs payload inspection in tests, introduce an interface wrapper rather than asserting through logs.
- This plan assumes introducing `CHANGELOG.md` and `docs/releases/...` is acceptable because no existing release-doc convention exists in this repo.
- If the broader product version is managed outside the `.csproj`, align the final version bump with the external release train before shipping.
