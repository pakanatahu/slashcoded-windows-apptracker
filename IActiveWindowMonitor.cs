namespace Slashcoded.DesktopTracker;

public interface IActiveWindowMonitor
{
    DesktopWindowSample? TryCapture();
}
