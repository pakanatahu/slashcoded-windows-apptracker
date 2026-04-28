using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
namespace Slashcoded.DesktopTracker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan AllowlistTtl = TimeSpan.FromMinutes(1);
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITrustedUploadClient _trustedUploadClient;
    private readonly IHostTrackingConfigProvider _hostTrackingConfigProvider;
    private readonly ISystemClock _clock;
    private readonly IIdleMonitor _idleMonitor;
    private readonly TrackerOptions _options;
    private readonly IActiveWindowMonitor _monitor;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _sleepGapThreshold;
    private readonly Dictionary<string, bool> _allowlist = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedDiscoveries = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _allowlistExpires = DateTimeOffset.MinValue;
    private DesktopWindowSample? _activeSample;
    private DateTimeOffset? _activeStart;
    private volatile bool _sleeping;
    private volatile bool _resetAfterResume;
    private DateTimeOffset _lastLoop = DateTimeOffset.MinValue;
    private DateTimeOffset _nextConfigRefresh = DateTimeOffset.MinValue;
    private bool _configInitialized;

    public Worker(
        IHttpClientFactory httpClientFactory,
        ITrustedUploadClient trustedUploadClient,
        IHostTrackingConfigProvider hostTrackingConfigProvider,
        ISystemClock clock,
        IIdleMonitor idleMonitor,
        IActiveWindowMonitor monitor,
        IOptions<TrackerOptions> options,
        ILogger<Worker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _trustedUploadClient = trustedUploadClient;
        _hostTrackingConfigProvider = hostTrackingConfigProvider;
        _clock = clock;
        _idleMonitor = idleMonitor;
        _options = options.Value;
        _logger = logger;
        _heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSeconds));
        _sleepGapThreshold = TimeSpan.FromMinutes(Math.Max(1, _options.SleepGapThresholdMinutes));
        _monitor = monitor;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TickAsync(stoppingToken);
                await Task.Delay(_heartbeatInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            await FlushElapsedAsync(_clock.Now, CancellationToken.None, true, _hostTrackingConfigProvider.Current);
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!_configInitialized)
        {
            await _hostTrackingConfigProvider.InitializeAsync(cancellationToken);
            _configInitialized = true;
            _nextConfigRefresh = _clock.Now.Add(_hostTrackingConfigProvider.RefreshInterval);
        }

        if (_sleeping)
        {
            _lastLoop = DateTimeOffset.MinValue;
            return;
        }

        if (_resetAfterResume)
        {
            ResetActiveTracking();
            _resetAfterResume = false;
            _lastLoop = DateTimeOffset.MinValue;
        }

        var sample = _monitor.TryCapture();
        var now = sample?.CapturedAt ?? _clock.Now;

        if (now >= _nextConfigRefresh)
        {
            await _hostTrackingConfigProvider.RefreshAsync(cancellationToken);
            _nextConfigRefresh = now.Add(_hostTrackingConfigProvider.RefreshInterval);
        }

        var config = _hostTrackingConfigProvider.Current;

        if (_lastLoop != DateTimeOffset.MinValue)
        {
            var gap = now - _lastLoop;
            if (gap > _sleepGapThreshold)
            {
                _logger.LogInformation("Detected long inactivity gap ({Gap}) - resetting tracker state", gap);
                ResetActiveTracking();
                _lastLoop = now;
                return;
            }
        }

        var idleDuration = _idleMonitor.GetIdleDuration();
        if (idleDuration >= TimeSpan.FromSeconds(config.IdleThresholdSeconds))
        {
            var idleCutoff = now.Subtract(idleDuration).AddSeconds(config.IdleThresholdSeconds);
            if (_activeStart.HasValue && idleCutoff < _activeStart.Value)
            {
                idleCutoff = _activeStart.Value;
            }

            await FlushElapsedAsync(idleCutoff, cancellationToken, true, config);
            ResetActiveTracking();
            _lastLoop = now;
            return;
        }

        if (_activeSample is null)
        {
            if (sample is not null)
            {
                await StartActiveSampleAsync(sample, now, cancellationToken);
            }

            _lastLoop = now;
            return;
        }

        if (sample is not null && HasChanged(sample))
        {
            await FlushElapsedAsync(now, cancellationToken, true, config);
            await StartActiveSampleAsync(sample, now, cancellationToken);
        }
        else
        {
            await FlushElapsedAsync(now, cancellationToken, false, config);
        }

        _lastLoop = now;
    }

    private async Task StartActiveSampleAsync(DesktopWindowSample sample, DateTimeOffset start, CancellationToken cancellationToken)
    {
        _activeSample = sample;
        await ReportDiscoveryIfNeededAsync(sample, cancellationToken);
        _activeStart = start;
        _logger.LogInformation("Active window: {Process} ({Path})",
            sample.ProcessName,
            sample.ProcessPath ?? "unknown");
    }

    private void ResetActiveTracking()
    {
        _activeSample = null;
        _activeStart = null;
    }

    private bool HasChanged(DesktopWindowSample sample)
    {
        if (_activeSample is null)
        {
            return true;
        }

        return !string.Equals(sample.ProcessName, _activeSample.ProcessName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sample.ProcessPath, _activeSample.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    private async Task FlushElapsedAsync(DateTimeOffset now, CancellationToken cancellationToken, bool flushAll, HostTrackingConfig config)
    {
        if (_activeSample is null || !_activeStart.HasValue)
        {
            return;
        }

        var start = _activeStart.Value;
        var elapsed = now - start;

        var segmentDuration = TimeSpan.FromSeconds(config.SegmentDurationSeconds);

        while (elapsed >= segmentDuration)
        {
            var segmentEnd = start.Add(segmentDuration);
            await PublishEventAsync(_activeSample, start, segmentEnd, config, cancellationToken);
            start = segmentEnd;
            elapsed = now - start;
        }

        if (flushAll && elapsed > TimeSpan.Zero)
        {
            await PublishEventAsync(_activeSample, start, now, config, cancellationToken);
            start = now;
        }

        _activeStart = start;
    }

    private async Task PublishEventAsync(DesktopWindowSample sample, DateTimeOffset segmentStart, DateTimeOffset segmentEnd, HostTrackingConfig config, CancellationToken cancellationToken)
    {
        var duration = segmentEnd - segmentStart;
        if (duration < TimeSpan.FromSeconds(1))
        {
            _logger.LogDebug("Skipping upload below minimum duration for {Process}", sample.ProcessName);
            return;
        }

        var normalizedProcessName = TrackingEventBuilder.NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
        var payload = TrackingEventBuilder.Build(sample, segmentStart, segmentEnd, config);
        if (payload is null)
        {
            _logger.LogDebug("Skipping zero-length segment for {Process}", normalizedProcessName);
            return;
        }

        var durationSeconds = Math.Max(1, (int)Math.Round(payload.Events[0].DurationMs / 1000.0));

        try
        {
            await _trustedUploadClient.PostSignedJsonAsync(HttpMethod.Post, "/api/upload", payload, cancellationToken);
            _logger.LogInformation("Uploaded {Process} activity: {Seconds}s", sample.ProcessName, durationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload desktop event");
        }
    }

    private async Task ReportDiscoveryIfNeededAsync(DesktopWindowSample sample, CancellationToken cancellationToken)
    {
        await EnsureAllowlistAsync(cancellationToken);
        if (_allowlist.ContainsKey(TrackingEventBuilder.NormalizeProcessName(sample.ProcessName, sample.ProcessPath)))
        {
            return;
        }

        await ReportDiscoveryAsync(sample, cancellationToken);
    }

    private async Task EnsureAllowlistAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _allowlistExpires)
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_options.ApiBaseUrl}/api/desktop/apps/allowlist", cancellationToken);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<DesktopAllowlistResponse>(cancellationToken: cancellationToken) ?? new DesktopAllowlistResponse(Array.Empty<DesktopAllowlistEntry>());
            _allowlist.Clear();
            foreach (var app in dto.Apps)
            {
                if (!string.IsNullOrWhiteSpace(app.ProcessName))
                {
                    _allowlist[TrackingEventBuilder.NormalizeProcessName(app.ProcessName, null)] = true;
                }
            }
            _allowlistExpires = DateTimeOffset.UtcNow.Add(AllowlistTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh desktop app allowlist");
            _allowlistExpires = DateTimeOffset.UtcNow.AddSeconds(10);
        }
    }

    private async Task ReportDiscoveryAsync(DesktopWindowSample sample, CancellationToken cancellationToken)
    {
        var normalizedProcessName = TrackingEventBuilder.NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
        if (!_reportedDiscoveries.Add(normalizedProcessName))
        {
            return;
        }

        try
        {
            var displayName = ResolveDisplayName(sample);
            var payload = new
            {
                processName = normalizedProcessName,
                displayName
            };
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync($"{_options.ApiBaseUrl}/api/desktop/apps/discover", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to report desktop app discovery for {App}", normalizedProcessName);
            _reportedDiscoveries.Remove(normalizedProcessName);
        }
    }

    private static string ResolveDisplayName(DesktopWindowSample sample) =>
        TrackingEventBuilder.Build(sample, sample.CapturedAt, sample.CapturedAt.AddSeconds(1), HostTrackingConfig.Default)
            ?.Events[0]
            .Payload
            .DisplayName ?? sample.ProcessName;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _sleeping = true;
            _logger.LogInformation("System suspend detected, pausing desktop tracker");
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _sleeping = false;
            _resetAfterResume = true;
            _logger.LogInformation("System resume detected, resetting desktop tracker state");
        }
    }

    public override void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        base.Dispose();
    }

    private sealed record DesktopAllowlistResponse(IReadOnlyList<DesktopAllowlistEntry> Apps);
    private sealed record DesktopAllowlistEntry(string ProcessName, string? DisplayName, string? Category);
}
