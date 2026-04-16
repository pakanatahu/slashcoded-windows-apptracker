namespace Slashcoded.DesktopTracker.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slashcoded.DesktopTracker;
using Slashcoded.DesktopTracker.Tests.Fakes;
using Xunit;

public sealed class WorkerTimingTests
{
    [Fact]
    public async Task ContinuousFocus_EmitsSlicesNoLongerThanConfiguredSegmentDuration()
    {
        var fixture = CreateFixture(new HostTrackingConfig(15, 300, "v1", DateTimeOffset.Parse("2026-04-14T00:00:00Z")));

        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(15));
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        var upload = Assert.Single(fixture.UploadClient.Payloads);
        var request = Assert.IsType<TrackingUploadRequest>(upload);
        Assert.Equal(15_000, request.Events[0].DurationMs);
        Assert.Equal(15, request.Events[0].Payload.SegmentDurationSeconds);
        Assert.Equal("v1", request.Events[0].Payload.TrackerConfigVersion);
    }

    [Fact]
    public async Task ProcessChange_ClosesCurrentSegmentAndStartsNewSegment()
    {
        var fixture = CreateFixture(HostTrackingConfig.Default);

        fixture.Monitor.Current = Sample(fixture.Clock.Now, processName: "chrome", title: "One");
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        fixture.Monitor.Current = Sample(fixture.Clock.Now, processName: "msedge", title: "Two");
        await fixture.Worker.TickAsync(CancellationToken.None);

        var upload = Assert.Single(fixture.UploadClient.Payloads);
        var request = Assert.IsType<TrackingUploadRequest>(upload);
        Assert.Equal(5_000, request.Events[0].DurationMs);
        Assert.Equal("chrome.exe", request.Events[0].Payload.ProcessName);
    }

    [Fact]
    public async Task IdleThreshold_StopsEmittingFocusedSlices()
    {
        var fixture = CreateFixture(new HostTrackingConfig(15, 5, "idle-v1", DateTimeOffset.Parse("2026-04-14T00:00:00Z")));

        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        fixture.IdleMonitor.IdleDuration = TimeSpan.FromSeconds(5);
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        var upload = Assert.Single(fixture.UploadClient.Payloads);
        var request = Assert.IsType<TrackingUploadRequest>(upload);
        Assert.Equal(5_000, request.Events[0].DurationMs);
        Assert.Equal(5, request.Events[0].Payload.IdleThresholdSeconds);
    }

    [Fact]
    public async Task IdleReturn_StartsNewSegment_InsteadOfExtendingPreIdleSegment()
    {
        var fixture = CreateFixture(new HostTrackingConfig(15, 5, "idle-v1", DateTimeOffset.Parse("2026-04-14T00:00:00Z")));

        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        fixture.IdleMonitor.IdleDuration = TimeSpan.FromSeconds(5);
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        fixture.IdleMonitor.IdleDuration = TimeSpan.Zero;
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromSeconds(15));
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        Assert.Equal(2, fixture.UploadClient.Payloads.Count);
        var first = Assert.IsType<TrackingUploadRequest>(fixture.UploadClient.Payloads[0]).Events[0];
        var second = Assert.IsType<TrackingUploadRequest>(fixture.UploadClient.Payloads[1]).Events[0];
        Assert.Equal(5_000, first.DurationMs);
        Assert.Equal(15_000, second.DurationMs);
        Assert.True(second.Payload.SegmentStartTs > first.Payload.SegmentEndTs);
    }

    [Fact]
    public async Task LongLoopGap_DoesNotBackfillOversizedSlice()
    {
        var fixture = CreateFixture(HostTrackingConfig.Default);

        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        Assert.Empty(fixture.UploadClient.Payloads);
    }

    [Fact]
    public async Task TickAsync_RefreshesConfigEveryFiveMinutes()
    {
        var fixture = CreateFixture(HostTrackingConfig.Default);

        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        fixture.Monitor.Current = Sample(fixture.Clock.Now);
        await fixture.Worker.TickAsync(CancellationToken.None);

        Assert.Equal(1, fixture.ConfigProvider.InitializeCount);
        Assert.Equal(1, fixture.ConfigProvider.RefreshCount);
    }

    private static WorkerFixture CreateFixture(HostTrackingConfig config)
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-14T09:15:00Z"));
        var idleMonitor = new FakeIdleMonitor();
        var monitor = new FakeActiveWindowMonitor();
        var uploadClient = new FakeTrustedUploadClient();
        var configProvider = new FakeHostTrackingConfigProvider
        {
            Current = config
        };
        var httpFactory = new StaticHttpClientFactory(new HttpClient(new AllowlistMessageHandler()));
        var options = Options.Create(new TrackerOptions
        {
            ApiBaseUrl = "http://127.0.0.1:5292",
            HeartbeatIntervalSeconds = 5,
            SleepGapThresholdMinutes = 5
        });
        var worker = new Worker(
            httpFactory,
            uploadClient,
            configProvider,
            clock,
            idleMonitor,
            monitor,
            options,
            NullLogger<Worker>.Instance);

        return new WorkerFixture(worker, clock, idleMonitor, monitor, uploadClient, configProvider);
    }

    private static DesktopWindowSample Sample(DateTimeOffset capturedAt, string processName = "chrome", string title = "Pull requests")
    {
        return new DesktopWindowSample(
            ProcessName: processName,
            ProcessPath: $@"C:\Program Files\{processName}\{processName}.exe",
            WindowTitle: title,
            CapturedAt: capturedAt);
    }

    private sealed record WorkerFixture(
        Worker Worker,
        FakeClock Clock,
        FakeIdleMonitor IdleMonitor,
        FakeActiveWindowMonitor Monitor,
        FakeTrustedUploadClient UploadClient,
        FakeHostTrackingConfigProvider ConfigProvider);

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class AllowlistMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Method == HttpMethod.Get
                ? """{"apps":[{"processName":"chrome.exe","displayName":"Chrome","category":"app"},{"processName":"msedge.exe","displayName":"Edge","category":"app"}]}"""
                : "{}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
