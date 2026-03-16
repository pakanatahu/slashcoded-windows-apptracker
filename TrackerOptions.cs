namespace Slashcoded.DesktopTracker;

public sealed class TrackerOptions
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5292";
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public int FlushIntervalSeconds { get; set; } = 15;
    public int FlushIntervalMinutes { get; set; } = 1;
    public int SleepGapThresholdMinutes { get; set; } = 5;
}
