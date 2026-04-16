namespace Slashcoded.DesktopTracker;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
