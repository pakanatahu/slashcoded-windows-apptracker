using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
namespace Slashcoded.DesktopTracker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan AllowlistTtl = TimeSpan.FromMinutes(1);
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrackerOptions _options;
    private readonly ActiveWindowMonitor _monitor;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _sleepGapThreshold;
    private readonly Dictionary<string, bool> _allowlist = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedDiscoveries = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _allowlistExpires = DateTimeOffset.MinValue;
    private DesktopWindowSample? _activeSample;
    private DateTimeOffset? _activeStart;
    private bool _activeAllowed;
    private volatile bool _sleeping;
    private volatile bool _resetAfterResume;
    private DateTimeOffset _lastLoop = DateTimeOffset.MinValue;

    public Worker(IHttpClientFactory httpClientFactory, IOptions<TrackerOptions> options, ILogger<Worker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _monitor = new ActiveWindowMonitor();
        _heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSeconds));
        _flushInterval = TimeSpan.FromMinutes(Math.Max(1, _options.FlushIntervalMinutes));
        _sleepGapThreshold = TimeSpan.FromMinutes(Math.Max(1, _options.SleepGapThresholdMinutes));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_sleeping)
            {
                _lastLoop = DateTimeOffset.MinValue;
                await Task.Delay(_heartbeatInterval, stoppingToken);
                continue;
            }

            if (_resetAfterResume)
            {
                _activeSample = null;
                _activeStart = null;
                _activeAllowed = false;
                _resetAfterResume = false;
                _lastLoop = DateTimeOffset.MinValue;
            }

            var sample = _monitor.TryCapture();
            var now = sample?.CapturedAt ?? DateTimeOffset.Now;
            if (_lastLoop != DateTimeOffset.MinValue)
            {
                var gap = now - _lastLoop;
                if (gap > _sleepGapThreshold)
                {
                    _logger.LogInformation("Detected long inactivity gap ({Gap}) - resetting tracker state", gap);
                    _activeSample = null;
                    _activeStart = null;
                    _activeAllowed = false;
                }
            }
            _lastLoop = now;

            if (_activeSample is null && sample is not null)
            {
                _activeSample = sample;
                _activeAllowed = await EvaluatePermissionAsync(sample, stoppingToken);
                _activeStart = now;
                _logger.LogInformation("Active window: {Process} ({Path}) - {Title}",
                    sample.ProcessName,
                    sample.ProcessPath ?? "unknown",
                    sample.WindowTitle);
            }

            if (_activeSample is not null && _activeStart.HasValue)
            {
                await FlushElapsedAsync(now, stoppingToken, false);
            }

            if (sample is not null && HasChanged(sample))
            {
                await FlushElapsedAsync(now, stoppingToken, true);
                _activeSample = sample;
                _activeAllowed = await EvaluatePermissionAsync(sample, stoppingToken);
                _activeStart = now;
                _logger.LogInformation("Active window: {Process} ({Path}) - {Title}",
                    sample.ProcessName,
                    sample.ProcessPath ?? "unknown",
                    sample.WindowTitle);
            }

            await Task.Delay(_heartbeatInterval, stoppingToken);
        }

        await FlushElapsedAsync(DateTimeOffset.Now, stoppingToken, true);
    }

    private bool HasChanged(DesktopWindowSample sample)
    {
        if (_activeSample is null)
        {
            return true;
        }

        return !string.Equals(sample.ProcessName, _activeSample.ProcessName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sample.WindowTitle, _activeSample.WindowTitle, StringComparison.Ordinal)
            || !string.Equals(sample.ProcessPath, _activeSample.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    private async Task FlushElapsedAsync(DateTimeOffset now, CancellationToken cancellationToken, bool flushAll)
    {
        if (_activeSample is null || !_activeStart.HasValue)
        {
            return;
        }

        await RefreshActivePermissionAsync(cancellationToken);
        if (!_activeAllowed)
        {
            _activeStart = now;
            return;
        }

        var start = _activeStart.Value;
        var elapsed = now - start;

        while (elapsed >= _flushInterval)
        {
            await PublishEventAsync(_activeSample, _flushInterval, start, cancellationToken);
            start = start.Add(_flushInterval);
            elapsed = now - start;
        }

        if (flushAll && elapsed > TimeSpan.Zero)
        {
            await PublishEventAsync(_activeSample, elapsed, start, cancellationToken);
            start = now;
        }

        _activeStart = start;
    }

    private async Task PublishEventAsync(DesktopWindowSample sample, TimeSpan duration, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        var durationMs = Math.Max(1, (long)Math.Round(duration.TotalSeconds * 1000));
        var durationSeconds = Math.Max(1, (int)Math.Round(duration.TotalSeconds));
        var payload = new
        {
            events = new[]
            {
                new {
                    userId = _options.UserId ?? "local",
                    source = "desktop",
                    occurredAt = occurredAt.ToString("O"),
                    durationMs = durationMs,
                    processName = sample.ProcessName,
                    payload = new {
                        processPath = sample.ProcessPath,
                        windowTitle = sample.WindowTitle,
                        duration_ms = durationMs
                    }
                }
            }
        };

        try
        {
            var response = await client.PostAsJsonAsync($"{_options.ApiBaseUrl}/api/upload", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Uploaded {Process} activity: {Seconds}s", sample.ProcessName, durationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload desktop event");
        }
    }

    private async Task<bool> EvaluatePermissionAsync(DesktopWindowSample sample, CancellationToken cancellationToken)
    {
        await EnsureAllowlistAsync(cancellationToken);
        if (_allowlist.ContainsKey(sample.ProcessName))
        {
            return true;
        }
        await ReportDiscoveryAsync(sample, cancellationToken);
        return false;
    }

    private async Task RefreshActivePermissionAsync(CancellationToken cancellationToken)
    {
        if (_activeSample is null)
        {
            _activeAllowed = false;
            return;
        }
        await EnsureAllowlistAsync(cancellationToken);
        _activeAllowed = _allowlist.ContainsKey(_activeSample.ProcessName);
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
                _allowlist[app.ProcessName] = true;
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
        if (!_reportedDiscoveries.Add(sample.ProcessName))
        {
            return;
        }

        try
        {
            var payload = new
            {
                processName = sample.ProcessName,
                displayName = sample.ProcessName
            };
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync($"{_options.ApiBaseUrl}/api/desktop/apps/discover", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to report desktop app discovery for {App}", sample.ProcessName);
            _reportedDiscoveries.Remove(sample.ProcessName);
        }
    }

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
