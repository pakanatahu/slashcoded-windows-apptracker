using System.Diagnostics;
using System.IO;

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

        var durationMs = segmentEnd.ToUnixTimeMilliseconds() - segmentStart.ToUnixTimeMilliseconds();
        if (durationMs <= 0)
        {
            return null;
        }

        var normalizedProcessName = NormalizeProcessName(sample.ProcessName, sample.ProcessPath);
        var timezone = TimezoneMetadataProvider.Capture(segmentEnd);

        return new TrackingUploadRequest(
            ContractVersion: "v3",
            Events:
            [
                new TrackingUploadEvent(
                    Kind: "app",
                    Producer: "desktop",
                    OccurredAt: segmentEnd.ToUniversalTime().ToString("O"),
                    DurationMs: durationMs,
                    ProcessName: normalizedProcessName,
                    DisplayName: ResolveDisplayName(sample),
                    TrackerConfigVersion: config.ConfigVersion,
                    SegmentDurationSeconds: config.SegmentDurationSeconds,
                    IdleThresholdSeconds: config.IdleThresholdSeconds,
                    Timezone: timezone.Timezone,
                    TimezoneOffsetMinutes: timezone.TimezoneOffsetMinutes,
                    TimezoneSource: timezone.TimezoneSource,
                    WindowsTimezone: timezone.WindowsTimezone)
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
}
