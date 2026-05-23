namespace Slashcoded.DesktopObserver;

public sealed record HostTrackingConfigResponse(
    int SegmentDurationSeconds,
    int IdleThresholdSeconds,
    string? ConfigVersion,
    DateTimeOffset? UpdatedAt);
