using System.Runtime.InteropServices;

namespace Slashcoded.DesktopTracker;

public sealed class WindowsIdleMonitor : IIdleMonitor
{
    public TimeSpan GetIdleDuration()
    {
        var info = new LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var currentTick = unchecked((uint)Environment.TickCount);
        var idleMilliseconds = unchecked(currentTick - info.DwTime);
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }
}
