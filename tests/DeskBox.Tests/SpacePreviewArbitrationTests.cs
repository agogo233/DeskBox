using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class SpacePreviewArbitrationTests
{
    [Fact]
    public void Resolve_QuickLookRunning_Wins()
    {
        Assert.Equal(
            SpacePreviewBackend.QuickLook,
            SpacePreviewArbitration.Resolve(
                quickLookAvailable: true,
                peekAvailable: false));
    }

    [Fact]
    public void Resolve_BothRunning_QuickLookWins()
    {
        // The dual-install priority pin: both apps running must keep
        // today's QuickLook behavior unchanged.
        Assert.Equal(
            SpacePreviewBackend.QuickLook,
            SpacePreviewArbitration.Resolve(
                quickLookAvailable: true,
                peekAvailable: true));
    }

    [Fact]
    public void Resolve_OnlyPeekRunning_PicksPeek()
    {
        Assert.Equal(
            SpacePreviewBackend.PowerToysPeek,
            SpacePreviewArbitration.Resolve(
                quickLookAvailable: false,
                peekAvailable: true));
    }

    [Fact]
    public void Resolve_NeitherRunning_PicksNone()
    {
        Assert.Equal(
            SpacePreviewBackend.None,
            SpacePreviewArbitration.Resolve(
                quickLookAvailable: false,
                peekAvailable: false));
    }

    [Fact]
    public void ResolveSwitchBackend_KeepsStickyPeekEvenWhenQuickLookAppears()
    {
        // A live session must keep the backend it opened with: flipping
        // mid-session would strand the first backend's preview window.
        Assert.Equal(
            SpacePreviewBackend.PowerToysPeek,
            SpacePreviewArbitration.ResolveSwitchBackend(
                stickyBackend: SpacePreviewBackend.PowerToysPeek,
                quickLookAvailable: true,
                peekAvailable: true));
    }

    [Fact]
    public void ResolveSwitchBackend_KeepsStickyQuickLookWhenPeekAppears()
    {
        Assert.Equal(
            SpacePreviewBackend.QuickLook,
            SpacePreviewArbitration.ResolveSwitchBackend(
                stickyBackend: SpacePreviewBackend.QuickLook,
                quickLookAvailable: true,
                peekAvailable: true));
    }

    [Fact]
    public void ResolveSwitchBackend_NoSession_FallsBackToFreshResolution()
    {
        Assert.Equal(
            SpacePreviewBackend.QuickLook,
            SpacePreviewArbitration.ResolveSwitchBackend(
                stickyBackend: null,
                quickLookAvailable: true,
                peekAvailable: true));
        Assert.Equal(
            SpacePreviewBackend.None,
            SpacePreviewArbitration.ResolveSwitchBackend(
                stickyBackend: null,
                quickLookAvailable: false,
                peekAvailable: false));
    }

    [Fact]
    public void ResolveSwitchBackend_NoneSticky_FallsBackToFreshResolution()
    {
        Assert.Equal(
            SpacePreviewBackend.PowerToysPeek,
            SpacePreviewArbitration.ResolveSwitchBackend(
                stickyBackend: SpacePreviewBackend.None,
                quickLookAvailable: false,
                peekAvailable: true));
    }
}
