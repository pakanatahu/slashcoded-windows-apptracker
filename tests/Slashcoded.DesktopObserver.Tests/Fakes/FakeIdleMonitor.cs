namespace Slashcoded.DesktopObserver.Tests.Fakes;

using Slashcoded.DesktopObserver;

public sealed class FakeIdleMonitor : IIdleMonitor
{
    public TimeSpan IdleDuration { get; set; }

    public TimeSpan GetIdleDuration() => IdleDuration;
}
