namespace Slashcoded.DesktopTracker;

public sealed record TimezoneMetadata(
    string? Timezone,
    int TimezoneOffsetMinutes,
    string TimezoneSource,
    string WindowsTimezone);

public static class TimezoneMetadataProvider
{
    public static TimezoneMetadata Capture(DateTimeOffset occurredAt)
    {
        var local = TimeZoneInfo.Local;
        var windowsTimezone = local.Id;
        string? iana = null;

        try
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsTimezone, out var converted))
            {
                iana = converted;
            }
        }
        catch
        {
            iana = null;
        }

        var offset = (int)local.GetUtcOffset(occurredAt.UtcDateTime).TotalMinutes;
        return new TimezoneMetadata(iana, offset, "producer", windowsTimezone);
    }
}
