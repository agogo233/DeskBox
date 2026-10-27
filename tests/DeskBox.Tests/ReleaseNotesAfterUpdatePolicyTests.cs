using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Decision table for the post-update release-notes window. The stakes:
/// showing on every launch (nag) or showing on a fresh install (stealing the
/// onboarding flow's first impression) are both regressions.
/// </summary>
public sealed class ReleaseNotesAfterUpdatePolicyTests
{
    [Theory]
    [InlineData(false, true, "1.5.8", "1.5.9")]
    [InlineData(true, false, "1.5.8", "1.5.9")]
    [InlineData(true, true, "", "1.5.9")]
    [InlineData(true, true, "1.5.9", "1.5.9")]
    [InlineData(true, true, " 1.5.9 ", "1.5.9")]
    [InlineData(true, true, "1.5.8", "")]
    public void SuppressedCases_DoNotOpenWindow(
        bool enabled,
        bool onboardingDone,
        string? lastRunVersion,
        string currentVersion)
    {
        Assert.False(ReleaseNotesAfterUpdatePolicy.ShouldShowWindow(
            enabled, onboardingDone, lastRunVersion, currentVersion));
    }

    [Fact]
    public void NullLastRunVersion_IsSuppressed()
    {
        // A null marker is every existing install upgrading to the build that
        // introduces the field — no version change just happened from the
        // user's perspective, so the window stays closed and the marker is
        // stamped for the next real update.
        Assert.False(ReleaseNotesAfterUpdatePolicy.ShouldShowWindow(
            showReleaseNotesAfterUpdate: true,
            hasCompletedOnboarding: true,
            lastRunVersion: null,
            currentVersion: "1.6.0"));
    }

    [Fact]
    public void VersionChange_OpensWindow()
    {
        Assert.True(ReleaseNotesAfterUpdatePolicy.ShouldShowWindow(
            showReleaseNotesAfterUpdate: true,
            hasCompletedOnboarding: true,
            lastRunVersion: "1.5.9",
            currentVersion: "1.6.0"));
    }

    [Fact]
    public void VersionComparison_IgnoresSurroundingWhitespace()
    {
        Assert.True(ReleaseNotesAfterUpdatePolicy.ShouldShowWindow(
            showReleaseNotesAfterUpdate: true,
            hasCompletedOnboarding: true,
            lastRunVersion: " 1.5.9 ",
            currentVersion: "1.6.0"));
    }
}
