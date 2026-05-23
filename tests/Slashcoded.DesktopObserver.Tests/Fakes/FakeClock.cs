namespace Slashcoded.DesktopObserver.Tests.Fakes;

using Slashcoded.DesktopObserver;

public sealed class FakeClock(DateTimeOffset now) : ISystemClock
{
    public DateTimeOffset Now { get; private set; } = now;

    public void Advance(TimeSpan interval)
    {
        Now = Now.Add(interval);
    }

    public void Set(DateTimeOffset now)
    {
        Now = now;
    }
}
