using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class PowerToysPeekPreviewServiceTests
{
    [Fact]
    public void BuildShellIntegrityArguments_QuotesSinglePathArgument()
    {
        const string path = @"C:\Work\preview file.pdf";

        Assert.Equal("\"C:\\Work\\preview file.pdf\"",
            PowerToysPeekPreviewService.BuildShellIntegrityArguments(path));
    }

    [Theory]
    [InlineData("0.96", true)]          // first CLI-capable release
    [InlineData("0.96.0", true)]
    [InlineData("0.96.1", true)]
    [InlineData("0.102.2933.0", true)]
    [InlineData("  0.97.1  ", true)]    // registry values can carry padding
    [InlineData("1.0.0.0", true)]
    [InlineData("0.95", false)]
    [InlineData("0.95.9", false)]
    [InlineData("0.90.0.0", false)]     // pre-CLI install runs the same process
    [InlineData(null, false)]           // unresolvable version fails closed
    [InlineData("", false)]
    [InlineData("not-a-version", false)]
    public void IsVersionSufficient_MatchesCliAvailabilityFloor(
        string? versionText,
        bool expected)
    {
        Assert.Equal(
            expected,
            PowerToysPeekPreviewService.IsVersionSufficient(versionText));
    }

    [Fact]
    public void ShouldSendSwitch_TrustsRecentToggleWithoutProbe()
    {
        // Peek's window takes a few dozen ms to appear after activation;
        // an immediate follow-up switch must not be eaten by the probe.
        Assert.True(PowerToysPeekPreviewService.ShouldSendSwitch(
            peekWindowVisible: false,
            lastToggleStartedTicks: 1_000,
            nowTicks: 1_000 + PowerToysPeekPreviewService.ToggleGracePeriodMs - 1));
    }

    [Fact]
    public void ShouldSendSwitch_SendsWhenWindowVisibleAfterGrace()
    {
        Assert.True(PowerToysPeekPreviewService.ShouldSendSwitch(
            peekWindowVisible: true,
            lastToggleStartedTicks: 1_000,
            nowTicks: 1_000 + PowerToysPeekPreviewService.ToggleGracePeriodMs + 1));
    }

    [Fact]
    public void ShouldSendSwitch_SuppressesWhenWindowHiddenAfterGrace()
    {
        // The core probe property: a user-closed Peek window must not be
        // reopened by selection follow updates.
        Assert.False(PowerToysPeekPreviewService.ShouldSendSwitch(
            peekWindowVisible: false,
            lastToggleStartedTicks: 1_000,
            nowTicks: 1_000 + PowerToysPeekPreviewService.ToggleGracePeriodMs + 1));
    }

    [Fact]
    public void ShouldSendSwitch_WithoutPriorToggleFollowsVisibilityOnly()
    {
        Assert.True(PowerToysPeekPreviewService.ShouldSendSwitch(
            peekWindowVisible: true,
            lastToggleStartedTicks: 0,
            nowTicks: 5_000));
        Assert.False(PowerToysPeekPreviewService.ShouldSendSwitch(
            peekWindowVisible: false,
            lastToggleStartedTicks: 0,
            nowTicks: 5_000));
    }
}
