using System.Diagnostics;
using Vanara.PInvoke;

namespace Slashcoded.DesktopTracker;

public sealed class ActiveWindowMonitor : IActiveWindowMonitor
{
    public DesktopWindowSample? TryCapture()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            var process = Process.GetProcessById((int)pid);
            var path = SafeGetProcessPath(process);
            return new DesktopWindowSample(
                ProcessName: process.ProcessName,
                ProcessPath: path,
                CapturedAt: DateTimeOffset.Now);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SafeGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}

public record DesktopWindowSample(string ProcessName, string? ProcessPath, DateTimeOffset CapturedAt);
