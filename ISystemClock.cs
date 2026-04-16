namespace Slashcoded.DesktopTracker;

public interface ISystemClock
{
    DateTimeOffset Now { get; }
}
