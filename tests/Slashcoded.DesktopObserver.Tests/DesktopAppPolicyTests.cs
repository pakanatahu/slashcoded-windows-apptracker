namespace Slashcoded.DesktopObserver.Tests;

using Slashcoded.DesktopObserver;
using Xunit;

public sealed class DesktopAppPolicyTests
{
    [Fact]
    public void FromResponse_UsesNewPolicyFlagsAndSeparateAppLists()
    {
        var policy = DesktopAppPolicy.FromResponse(
            measureEnabled: false,
            discoveryEnabled: false,
            allowedApps:
            [
                new DesktopAppPolicyEntry("chrome.exe", "Chrome", IsAllowed: true, IsIgnored: false)
            ],
            ignoredApps:
            [
                new DesktopAppPolicyEntry("steam.exe", "Steam", IsAllowed: false, IsIgnored: true)
            ],
            legacyApps: []);

        Assert.False(policy.MeasureEnabled);
        Assert.False(policy.DiscoveryEnabled);
        Assert.True(policy.IsKnown("chrome", null));
        Assert.True(policy.IsKnown("steam", null));
        Assert.False(policy.IsRecordable("chrome", null));
        Assert.False(policy.IsRecordable("steam", null));
    }

    [Fact]
    public void FromResponse_DefaultsMissingFlagsToEnabledForLegacyResponses()
    {
        var policy = DesktopAppPolicy.FromResponse(
            measureEnabled: null,
            discoveryEnabled: null,
            allowedApps: [],
            ignoredApps: [],
            legacyApps:
            [
                new DesktopAppPolicyEntry("chrome.exe", "Chrome", IsAllowed: true, IsIgnored: false),
                new DesktopAppPolicyEntry("steam.exe", "Steam", IsAllowed: false, IsIgnored: true)
            ]);

        Assert.True(policy.MeasureEnabled);
        Assert.True(policy.DiscoveryEnabled);
        Assert.True(policy.IsRecordable("chrome", null));
        Assert.False(policy.IsRecordable("steam", null));
        Assert.True(policy.IsKnown("steam", null));
    }

    [Fact]
    public void FromResponse_TreatsLegacyUnallowedUnignoredAppsAsUnknown()
    {
        var policy = DesktopAppPolicy.FromResponse(
            measureEnabled: null,
            discoveryEnabled: null,
            allowedApps: [],
            ignoredApps: [],
            legacyApps:
            [
                new DesktopAppPolicyEntry("notepad.exe", "Notepad", IsAllowed: false, IsIgnored: false)
            ]);

        Assert.False(policy.IsKnown("notepad", null));
        Assert.False(policy.IsRecordable("notepad", null));
    }
}
