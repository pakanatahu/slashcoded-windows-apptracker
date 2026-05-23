namespace Slashcoded.DesktopObserver;

public interface IActiveWindowMonitor
{
    DesktopWindowSample? TryCapture();
}
