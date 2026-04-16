namespace Slashcoded.DesktopTracker.Tests.Fakes;

using Slashcoded.DesktopTracker;

public sealed class FakeIdleMonitor : IIdleMonitor
{
    public TimeSpan IdleDuration { get; set; }

    public TimeSpan GetIdleDuration() => IdleDuration;
}
