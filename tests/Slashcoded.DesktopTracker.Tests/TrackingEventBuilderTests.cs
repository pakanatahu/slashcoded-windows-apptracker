namespace Slashcoded.DesktopTracker.Tests;

using Slashcoded.DesktopTracker;
using System.Text.Json;
using Xunit;

public sealed class TrackingEventBuilderTests
{
    private static readonly DesktopWindowSample Sample = new(
        ProcessName: "chrome",
        ProcessPath: @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        CapturedAt: DateTimeOffset.Parse("2026-04-14T09:15:15Z"));

    [Fact]
    public void Build_AppEvent_IncludesSharedTimingMetadata()
    {
        var config = new HostTrackingConfig(15, 300, "2026-04-14T00:00:00.0000000Z", DateTimeOffset.Parse("2026-04-14T00:00:00Z"));

        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            DateTimeOffset.Parse("2026-04-14T09:15:30Z"),
            config);

        Assert.NotNull(request);
        var evt = request.Events[0];
        Assert.Equal("v3", request.ContractVersion);
        Assert.Equal("app", evt.Kind);
        Assert.Equal("desktop", evt.Producer);
        Assert.Equal("2026-04-14T00:00:00.0000000Z", evt.TrackerConfigVersion);
        Assert.Equal(15, evt.SegmentDurationSeconds);
        Assert.Equal(300, evt.IdleThresholdSeconds);
    }

    [Fact]
    public void Build_AppEvent_UsesSegmentEndAsOccurredAt()
    {
        var segmentEnd = DateTimeOffset.Parse("2026-04-14T09:15:30Z");

        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            segmentEnd,
            HostTrackingConfig.Default);

        Assert.NotNull(request);
        Assert.Equal(segmentEnd.ToUniversalTime().ToString("O"), request.Events[0].OccurredAt);
    }

    [Fact]
    public void Build_AppEvent_DurationMatchesSegmentTimestamps()
    {
        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            DateTimeOffset.Parse("2026-04-14T09:15:30Z"),
            HostTrackingConfig.Default);

        Assert.NotNull(request);
        Assert.Equal(15_000, request.Events[0].DurationMs);
    }

    [Fact]
    public void Build_AppEvent_IncludesProducerTimezoneMetadata()
    {
        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            DateTimeOffset.Parse("2026-04-14T09:15:30Z"),
            HostTrackingConfig.Default);

        Assert.NotNull(request);
        var evt = request.Events[0];
        Assert.Equal("producer", evt.TimezoneSource);
        Assert.NotNull(evt.WindowsTimezone);
        Assert.InRange(evt.TimezoneOffsetMinutes.GetValueOrDefault(), -720, 840);
    }

    [Fact]
    public void Build_AppEvent_DoesNotSerializeWindowTitle()
    {
        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            DateTimeOffset.Parse("2026-04-14T09:15:30Z"),
            HostTrackingConfig.Default);

        Assert.NotNull(request);
        var json = JsonSerializer.Serialize(request);
        Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_AppEvent_ClampsDurationToConfiguredSegmentDuration()
    {
        var config = new HostTrackingConfig(10, 300, "v1", DateTimeOffset.Parse("2026-04-14T00:00:00Z"));

        var request = TrackingEventBuilder.Build(
            Sample,
            DateTimeOffset.Parse("2026-04-14T09:15:15Z"),
            DateTimeOffset.Parse("2026-04-14T09:15:45Z"),
            config);

        Assert.NotNull(request);
        Assert.Equal(10_000, request.Events[0].DurationMs);
    }

    [Fact]
    public void Build_AppEvent_ReusesEventId_ForSameClosedSegment()
    {
        var start = DateTimeOffset.Parse("2026-04-14T09:15:15Z");
        var end = DateTimeOffset.Parse("2026-04-14T09:15:30Z");

        var first = TrackingEventBuilder.Build(Sample, start, end, HostTrackingConfig.Default);
        var second = TrackingEventBuilder.Build(Sample, start, end, HostTrackingConfig.Default);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Events[0], second.Events[0]);
    }

    [Fact]
    public void Build_AppEvent_ReturnsNull_ForNonPositiveDuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-04-14T09:15:15Z");

        var request = TrackingEventBuilder.Build(Sample, timestamp, timestamp, HostTrackingConfig.Default);

        Assert.Null(request);
    }
}
