namespace Slashcoded.DesktopTracker;

public sealed record HostTrackingConfig(
    int SegmentDurationSeconds,
    int IdleThresholdSeconds,
    string? ConfigVersion,
    DateTimeOffset? UpdatedAt)
{
    public static HostTrackingConfig Default { get; } = new(15, 300, null, null);
}
