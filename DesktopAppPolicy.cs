namespace Slashcoded.DesktopObserver;

public sealed class DesktopAppPolicy
{
    private readonly Dictionary<string, DesktopAppPolicyEntry> _entries;

    private DesktopAppPolicy(Dictionary<string, DesktopAppPolicyEntry> entries)
    {
        _entries = entries;
    }

    public static DesktopAppPolicy Empty { get; } = new(new Dictionary<string, DesktopAppPolicyEntry>(StringComparer.OrdinalIgnoreCase));

    public static DesktopAppPolicy FromEntries(IEnumerable<DesktopAppPolicyEntry> entries)
    {
        var map = new Dictionary<string, DesktopAppPolicyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ProcessName))
            {
                continue;
            }

            map[ObserverEventBuilder.NormalizeProcessName(entry.ProcessName, null)] = entry;
        }

        return new DesktopAppPolicy(map);
    }

    public bool IsRecordable(string processName, string? processPath)
    {
        var normalized = ObserverEventBuilder.NormalizeProcessName(processName, processPath);
        return _entries.TryGetValue(normalized, out var entry) && entry.IsAllowed && !entry.IsIgnored;
    }

    public bool IsKnown(string processName, string? processPath)
    {
        var normalized = ObserverEventBuilder.NormalizeProcessName(processName, processPath);
        return _entries.ContainsKey(normalized);
    }
}

public sealed record DesktopAppPolicyEntry(string ProcessName, string? DisplayName, bool IsAllowed, bool IsIgnored);
