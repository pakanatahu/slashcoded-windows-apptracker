namespace Slashcoded.DesktopObserver;

public sealed class DesktopAppPolicy
{
    private readonly Dictionary<string, DesktopAppPolicyEntry> _entries;

    private DesktopAppPolicy(
        bool measureEnabled,
        bool discoveryEnabled,
        Dictionary<string, DesktopAppPolicyEntry> entries)
    {
        MeasureEnabled = measureEnabled;
        DiscoveryEnabled = discoveryEnabled;
        _entries = entries;
    }

    public bool MeasureEnabled { get; }

    public bool DiscoveryEnabled { get; }

    public static DesktopAppPolicy Empty { get; } = new(
        measureEnabled: true,
        discoveryEnabled: true,
        new Dictionary<string, DesktopAppPolicyEntry>(StringComparer.OrdinalIgnoreCase));

    public static DesktopAppPolicy FromEntries(IEnumerable<DesktopAppPolicyEntry> entries)
    {
        return FromResponse(null, null, [], [], entries);
    }

    public static DesktopAppPolicy FromResponse(
        bool? measureEnabled,
        bool? discoveryEnabled,
        IEnumerable<DesktopAppPolicyEntry>? allowedApps,
        IEnumerable<DesktopAppPolicyEntry>? ignoredApps,
        IEnumerable<DesktopAppPolicyEntry>? legacyApps)
    {
        var map = new Dictionary<string, DesktopAppPolicyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allowedApps ?? [])
        {
            AddEntry(map, entry with { IsAllowed = true, IsIgnored = false });
        }

        foreach (var entry in ignoredApps ?? [])
        {
            AddEntry(map, entry with { IsAllowed = false, IsIgnored = true });
        }

        foreach (var entry in legacyApps ?? [])
        {
            if (entry.IsIgnored)
            {
                AddEntry(map, entry with { IsAllowed = false, IsIgnored = true });
            }
            else if (entry.IsAllowed)
            {
                AddEntry(map, entry with { IsAllowed = true, IsIgnored = false });
            }
        }

        return new DesktopAppPolicy(measureEnabled ?? true, discoveryEnabled ?? true, map);
    }

    private static void AddEntry(
        Dictionary<string, DesktopAppPolicyEntry> map,
        DesktopAppPolicyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ProcessName))
        {
            return;
        }

        map[ObserverEventBuilder.NormalizeProcessName(entry.ProcessName, null)] = entry;
    }

    public bool IsRecordable(string processName, string? processPath)
    {
        var normalized = ObserverEventBuilder.NormalizeProcessName(processName, processPath);
        return MeasureEnabled
            && _entries.TryGetValue(normalized, out var entry)
            && entry.IsAllowed
            && !entry.IsIgnored;
    }

    public bool IsKnown(string processName, string? processPath)
    {
        var normalized = ObserverEventBuilder.NormalizeProcessName(processName, processPath);
        return _entries.ContainsKey(normalized);
    }
}

public sealed record DesktopAppPolicyEntry(string ProcessName, string? DisplayName, bool IsAllowed, bool IsIgnored);
