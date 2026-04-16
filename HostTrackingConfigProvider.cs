using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Slashcoded.DesktopTracker;

public sealed class HostTrackingConfigProvider : IHostTrackingConfigProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrackerOptions _options;
    private readonly ILogger<HostTrackingConfigProvider> _logger;
    private HostTrackingConfig _current = HostTrackingConfig.Default;

    public HostTrackingConfigProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<TrackerOptions> options,
        ILogger<HostTrackingConfigProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public HostTrackingConfig Current => _current;

    public TimeSpan RefreshInterval { get; } = TimeSpan.FromMinutes(5);

    public Task InitializeAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _options.ApiBaseUrl.TrimEnd('/');

            using var handshake = await client.GetAsync($"{baseUrl}/api/host/handshake", cancellationToken);
            handshake.EnsureSuccessStatusCode();

            using var response = await client.GetAsync($"{baseUrl}/api/host/tracking-config", cancellationToken);
            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<HostTrackingConfigResponse>(cancellationToken: cancellationToken);
            if (dto is null)
            {
                throw new InvalidOperationException("Host tracking config response was empty.");
            }

            _current = Normalize(dto);
            _logger.LogInformation(
                "Loaded host tracking config: segment={SegmentDurationSeconds}s idle={IdleThresholdSeconds}s version={ConfigVersion}",
                _current.SegmentDurationSeconds,
                _current.IdleThresholdSeconds,
                _current.ConfigVersion ?? "unknown");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to refresh host tracking config; keeping segment={SegmentDurationSeconds}s idle={IdleThresholdSeconds}s version={ConfigVersion}",
                _current.SegmentDurationSeconds,
                _current.IdleThresholdSeconds,
                _current.ConfigVersion ?? "startup-default");
        }
    }

    private static HostTrackingConfig Normalize(HostTrackingConfigResponse response)
    {
        var defaults = HostTrackingConfig.Default;
        return new HostTrackingConfig(
            response.SegmentDurationSeconds > 0 ? response.SegmentDurationSeconds : defaults.SegmentDurationSeconds,
            response.IdleThresholdSeconds > 0 ? response.IdleThresholdSeconds : defaults.IdleThresholdSeconds,
            string.IsNullOrWhiteSpace(response.ConfigVersion) ? null : response.ConfigVersion,
            response.UpdatedAt);
    }
}
