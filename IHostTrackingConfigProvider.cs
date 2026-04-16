namespace Slashcoded.DesktopTracker;

public interface IHostTrackingConfigProvider
{
    HostTrackingConfig Current { get; }
    TimeSpan RefreshInterval { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
}
