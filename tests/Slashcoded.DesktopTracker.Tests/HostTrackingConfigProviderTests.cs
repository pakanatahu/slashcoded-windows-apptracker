namespace Slashcoded.DesktopTracker.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slashcoded.DesktopTracker;
using Xunit;

public sealed class HostTrackingConfigProviderTests
{
    [Fact]
    public async Task InitializeAsync_UsesDefaultsBeforeFirstSuccessfulFetch()
    {
        var provider = CreateProvider(new[]
        {
            Response(HttpStatusCode.InternalServerError, "{}")
        });

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Equal(15, provider.Current.SegmentDurationSeconds);
        Assert.Equal(300, provider.Current.IdleThresholdSeconds);
        Assert.Null(provider.Current.ConfigVersion);
    }

    [Fact]
    public async Task InitializeAsync_LoadsHostConfig_AfterHandshake()
    {
        var provider = CreateProvider(new[]
        {
            Response(HttpStatusCode.OK, """{"status":"ok"}"""),
            Response(HttpStatusCode.OK, """
            {
              "segmentDurationSeconds": 20,
              "idleThresholdSeconds": 420,
              "configVersion": "v1",
              "updatedAt": "2026-04-14T00:00:00.0000000Z"
            }
            """)
        });

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Equal(20, provider.Current.SegmentDurationSeconds);
        Assert.Equal(420, provider.Current.IdleThresholdSeconds);
        Assert.Equal("v1", provider.Current.ConfigVersion);
    }

    [Fact]
    public async Task RefreshAsync_KeepsLastKnownGoodConfig_WhenRefreshFails()
    {
        var provider = CreateProvider(new[]
        {
            Response(HttpStatusCode.OK, """{"status":"ok"}"""),
            Response(HttpStatusCode.OK, """
            {
              "segmentDurationSeconds": 20,
              "idleThresholdSeconds": 420,
              "configVersion": "v1",
              "updatedAt": "2026-04-14T00:00:00.0000000Z"
            }
            """),
            Response(HttpStatusCode.OK, """{"status":"ok"}"""),
            Response(HttpStatusCode.InternalServerError, "{}")
        });

        await provider.InitializeAsync(CancellationToken.None);
        await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(20, provider.Current.SegmentDurationSeconds);
        Assert.Equal(420, provider.Current.IdleThresholdSeconds);
        Assert.Equal("v1", provider.Current.ConfigVersion);
    }

    [Fact]
    public async Task RefreshAsync_NormalizesInvalidTimingValues_ToDefaults()
    {
        var provider = CreateProvider(new[]
        {
            Response(HttpStatusCode.OK, """{"status":"ok"}"""),
            Response(HttpStatusCode.OK, """
            {
              "segmentDurationSeconds": 0,
              "idleThresholdSeconds": -1,
              "configVersion": "bad",
              "updatedAt": "2026-04-14T00:00:00.0000000Z"
            }
            """)
        });

        await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(15, provider.Current.SegmentDurationSeconds);
        Assert.Equal(300, provider.Current.IdleThresholdSeconds);
        Assert.Equal("bad", provider.Current.ConfigVersion);
    }

    private static HostTrackingConfigProvider CreateProvider(IEnumerable<HttpResponseMessage> responses)
    {
        var client = new HttpClient(new QueueMessageHandler(responses))
        {
            BaseAddress = new Uri("http://127.0.0.1:5292")
        };
        var factory = new StaticHttpClientFactory(client);
        var options = Options.Create(new TrackerOptions { ApiBaseUrl = "http://127.0.0.1:5292" });

        return new HostTrackingConfigProvider(factory, options, NullLogger<HostTrackingConfigProvider>.Instance);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class QueueMessageHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                return Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}"));
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
