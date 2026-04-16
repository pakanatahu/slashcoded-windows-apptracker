namespace Slashcoded.DesktopTracker.Tests.Fakes;

using Slashcoded.DesktopTracker;

public sealed class FakeHostTrackingConfigProvider : IHostTrackingConfigProvider
{
    public HostTrackingConfig Current { get; set; } = HostTrackingConfig.Default;
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
    public int InitializeCount { get; private set; }
    public int RefreshCount { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        InitializeCount++;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        RefreshCount++;
        return Task.CompletedTask;
    }
}
