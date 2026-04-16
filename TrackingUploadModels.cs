using System.Text.Json.Serialization;

namespace Slashcoded.DesktopTracker;

public sealed record TrackingUploadRequest(
    string ContractVersion,
    IReadOnlyList<TrackingUploadEvent> Events);

public sealed record TrackingUploadEvent(
    string Source,
    string OccurredAt,
    long DurationMs,
    string? Project,
    string Category,
    AppTrackingPayload Payload);

public sealed record AppTrackingPayload(
    string Type,
    [property: JsonPropertyName("event_id")] string EventId,
    string Process,
    string ProcessName,
    string? ProcessPath,
    string DisplayName,
    string WindowTitle,
    [property: JsonPropertyName("segment_start_ts")] long SegmentStartTs,
    [property: JsonPropertyName("segment_end_ts")] long SegmentEndTs,
    string? TrackerConfigVersion,
    int SegmentDurationSeconds,
    int IdleThresholdSeconds);
