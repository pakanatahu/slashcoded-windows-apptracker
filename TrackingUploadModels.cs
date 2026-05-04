namespace Slashcoded.DesktopTracker;

public sealed record TrackingUploadRequest(
    string ContractVersion,
    IReadOnlyList<TrackingUploadEvent> Events);

public sealed record TrackingUploadEvent(
    string Kind,
    string Producer,
    string OccurredAt,
    long DurationMs,
    string ProcessName,
    string DisplayName,
    string? TrackerConfigVersion,
    int SegmentDurationSeconds,
    int IdleThresholdSeconds,
    string? Timezone = null,
    int? TimezoneOffsetMinutes = null,
    string? TimezoneSource = null,
    string? WindowsTimezone = null);
