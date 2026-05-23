namespace Slashcoded.DesktopObserver;

public sealed record ObserverUploadRequest(
    string ContractVersion,
    IReadOnlyList<ObserverUploadEvent> Events);

public sealed record ObserverUploadEvent(
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
