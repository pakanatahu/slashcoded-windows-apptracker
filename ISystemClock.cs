namespace Slashcoded.DesktopObserver;

public interface ISystemClock
{
    DateTimeOffset Now { get; }
}
