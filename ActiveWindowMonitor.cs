using System.Diagnostics;
using System.Text;
using Vanara.PInvoke;

namespace Slashcoded.DesktopTracker;

public sealed class ActiveWindowMonitor
{
    public DesktopWindowSample? TryCapture()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var titleLength = User32.GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(titleLength + 1);
        _ = User32.GetWindowText(hwnd, buffer, buffer.Capacity);
        var title = buffer.ToString();

        User32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            var process = Process.GetProcessById((int)pid);
            var path = SafeGetProcessPath(process);
            return new DesktopWindowSample(
                ProcessName: process.ProcessName,
                ProcessPath: path,
                WindowTitle: title,
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

public record DesktopWindowSample(string ProcessName, string? ProcessPath, string WindowTitle, DateTimeOffset CapturedAt);
