using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
namespace Slashcoded.DesktopTracker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan AllowlistTtl = TimeSpan.FromMinutes(1);
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrustedUploadClient _trustedUploadClient;
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
    private volatile bool _sleeping;
    private volatile bool _resetAfterResume;
    private DateTimeOffset _lastLoop = DateTimeOffset.MinValue;

    public Worker(
        IHttpClientFactory httpClientFactory,
        TrustedUploadClient trustedUploadClient,
        IOptions<TrackerOptions> options,
        ILogger<Worker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _trustedUploadClient = trustedUploadClient;
        _options = options.Value;
        _logger = logger;
        _monitor = new ActiveWindowMonitor();
        _heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatIntervalSeconds));
        _flushInterval = ResolveFlushInterval(_options);
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
                }
            }
            _lastLoop = now;

            if (_activeSample is null && sample is not null)
            {
                _activeSample = sample;
                await ReportDiscoveryIfNeededAsync(sample, stoppingToken);
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
                await ReportDiscoveryIfNeededAsync(sample, stoppingToken);
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

        var start = _activeStart.Value;
        var elapsed = now - start;

        while (elapsed >= _flushInterval)
        {
            var segmentEnd = start.Add(_flushInterval);
            await PublishEventAsync(_activeSample, start, segmentEnd, cancellationToken);
            start = segmentEnd;
            elapsed = now - start;
        }

        if (flushAll && elapsed > TimeSpan.Zero)
        {
            await PublishEventAsync(_activeSample, start, now, cancellationToken);
            start = now;
        }

        _activeStart = start;
    }

    private async Task PublishEventAsync(DesktopWindowSample sample, DateTimeOffset segmentStart, DateTimeOffset segmentEnd, CancellationToken cancellationToken)
    {
        var duration = segmentEnd - segmentStart;
        if (duration < TimeSpan.FromSeconds(1))
        {
            _logger.LogDebug("Skipping upload below minimum duration for {Process}", sample.ProcessName);
            return;
        }

        if (duration > TimeSpan.FromHours(24))
        {
            _logger.LogWarning("Clamping oversized activity duration for {Process} from {Duration} to 24h", sample.ProcessName, duration);
            duration = TimeSpan.FromHours(24);
            segmentEnd = segmentStart.Add(duration);
        }

        var normalizedProcessName = NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
        var processDisplayName = ResolveDisplayName(sample);
        var segmentStartTs = segmentStart.ToUnixTimeMilliseconds();
        var segmentEndTs = segmentEnd.ToUnixTimeMilliseconds();
        var durationMs = segmentEndTs - segmentStartTs;
        if (durationMs <= 0)
        {
            _logger.LogDebug("Skipping zero-length segment for {Process}", normalizedProcessName);
            return;
        }

        var durationSeconds = Math.Max(1, (int)Math.Round(duration.TotalSeconds));
        var payload = new
        {
            contractVersion = "v2",
            events = new[]
            {
                new {
                    source = "desktop",
                    occurredAt = segmentEnd.ToUniversalTime().ToString("O"),
                    durationMs = durationMs,
                    project = (string?)null,
                    category = "app",
                    payload = new {
                        type = "app",
                        event_id = BuildEventId(normalizedProcessName, sample.WindowTitle, segmentStartTs, segmentEndTs),
                        process = normalizedProcessName,
                        processName = normalizedProcessName,
                        processPath = sample.ProcessPath,
                        displayName = processDisplayName,
                        windowTitle = sample.WindowTitle,
                        segment_start_ts = segmentStartTs,
                        segment_end_ts = segmentEndTs
                    }
                }
            }
        };

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
        if (_allowlist.ContainsKey(NormalizeProcessName(sample.ProcessName, sample.ProcessPath)))
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
                    _allowlist[NormalizeProcessName(app.ProcessName, null)] = true;
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
        var normalizedProcessName = NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
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

    private static string ResolveDisplayName(DesktopWindowSample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.ProcessPath))
        {
            return sample.ProcessName;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(sample.ProcessPath);
            var description = info.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
            var product = info.ProductName;
            if (!string.IsNullOrWhiteSpace(product))
            {
                return product.Trim();
            }
        }
        catch
        {
            // ignore and fall back to process name
        }

        var fileName = Path.GetFileNameWithoutExtension(sample.ProcessPath);
        return string.IsNullOrWhiteSpace(fileName) ? sample.ProcessName : fileName;
    }

    private static TimeSpan ResolveFlushInterval(TrackerOptions options)
    {
        if (options.FlushIntervalSeconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Clamp(options.FlushIntervalSeconds, 5, 30));
        }

        if (options.FlushIntervalMinutes > 0)
        {
            return TimeSpan.FromSeconds(Math.Clamp(options.FlushIntervalMinutes * 60, 5, 30));
        }

        return TimeSpan.FromSeconds(15);
    }

    private static string NormalizeProcessName(string processName, string? processPath)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var fileName = Path.GetFileName(processPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return processName;
        }

        return processName + ".exe";
    }

    private static string BuildEventId(string processName, string windowTitle, long segmentStartTs, long segmentEndTs)
    {
        var key = string.Join("|",
            Environment.MachineName,
            processName,
            segmentStartTs.ToString(),
            segmentEndTs.ToString(),
            ComputeSha256Hex(windowTitle));
        return $"desktop-{ComputeSha256Hex(key)}";
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
