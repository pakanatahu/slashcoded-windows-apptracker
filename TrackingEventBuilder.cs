using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Slashcoded.DesktopTracker;

public static class TrackingEventBuilder
{
    public static TrackingUploadRequest? Build(
        DesktopWindowSample sample,
        DateTimeOffset segmentStart,
        DateTimeOffset segmentEnd,
        HostTrackingConfig config)
    {
        var maxSegmentEnd = segmentStart.AddSeconds(config.SegmentDurationSeconds);
        if (segmentEnd > maxSegmentEnd)
        {
            segmentEnd = maxSegmentEnd;
        }

        var segmentStartTs = segmentStart.ToUnixTimeMilliseconds();
        var segmentEndTs = segmentEnd.ToUnixTimeMilliseconds();
        var durationMs = segmentEndTs - segmentStartTs;
        if (durationMs <= 0)
        {
            return null;
        }

        var normalizedProcessName = NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
        var payload = new AppTrackingPayload(
            Type: "app",
            EventId: BuildEventId(normalizedProcessName, segmentStartTs, segmentEndTs),
            Process: normalizedProcessName,
            ProcessName: normalizedProcessName,
            ProcessPath: sample.ProcessPath,
            DisplayName: ResolveDisplayName(sample),
            SegmentStartTs: segmentStartTs,
            SegmentEndTs: segmentEndTs,
            TrackerConfigVersion: config.ConfigVersion,
            SegmentDurationSeconds: config.SegmentDurationSeconds,
            IdleThresholdSeconds: config.IdleThresholdSeconds);

        return new TrackingUploadRequest(
            ContractVersion: "v2",
            Events:
            [
                new TrackingUploadEvent(
                    Source: "desktop",
                    OccurredAt: segmentEnd.ToUniversalTime().ToString("O"),
                    DurationMs: durationMs,
                    Project: null,
                    Category: "app",
                    Payload: payload)
            ]);
    }

    public static string NormalizeProcessName(string processName, string? processPath)
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
            // Fall back to the executable name when version metadata is unavailable.
        }

        var fileName = Path.GetFileNameWithoutExtension(sample.ProcessPath);
        return string.IsNullOrWhiteSpace(fileName) ? sample.ProcessName : fileName;
    }

    private static string BuildEventId(string processName, long segmentStartTs, long segmentEndTs)
    {
        var key = string.Join("|",
            Environment.MachineName,
            processName,
            segmentStartTs.ToString(),
            segmentEndTs.ToString());
        return $"desktop-{ComputeSha256Hex(key)}";
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
