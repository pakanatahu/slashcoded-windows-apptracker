namespace Slashcoded.DesktopTracker.Tests.Fakes;

using Slashcoded.DesktopTracker;

public sealed class FakeActiveWindowMonitor : IActiveWindowMonitor
{
    private readonly Queue<DesktopWindowSample?> _samples = new();

    public void Enqueue(DesktopWindowSample? sample)
    {
        _samples.Enqueue(sample);
    }

    public DesktopWindowSample? Current { get; set; }

    public DesktopWindowSample? TryCapture()
    {
        if (_samples.Count > 0)
        {
            Current = _samples.Dequeue();
        }

        return Current;
    }
}
