using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pacing rules for the periodic update-check loop. The stakes: a failed
/// check that paces like a success silences update notices for a day
/// (deskbox.fun DNS blackouts are the documented field reality); a success
/// that retries in two hours polls the feed twelve times a day for nothing.
/// </summary>
public sealed class BackgroundUpdateCheckPolicyTests
{
    [Theory]
    [InlineData(AppUpdateCheckStatus.UpdateAvailable)]
    [InlineData(AppUpdateCheckStatus.UpToDate)]
    public void DefinitiveAnswers_WaitAFullDay(AppUpdateCheckStatus status)
    {
        Assert.True(BackgroundUpdateCheckPolicy.IsDefinitive(status));
        Assert.Equal(
            BackgroundUpdateCheckPolicy.DefinitiveAnswerInterval,
            BackgroundUpdateCheckPolicy.GetDelayUntilNextCheck(status));
    }

    [Theory]
    [InlineData(AppUpdateCheckStatus.Failed)]
    [InlineData(AppUpdateCheckStatus.InvalidManifest)]
    public void OpenQuestions_RetryInTwoHours(AppUpdateCheckStatus status)
    {
        Assert.False(BackgroundUpdateCheckPolicy.IsDefinitive(status));
        Assert.Equal(
            BackgroundUpdateCheckPolicy.FailureRetryInterval,
            BackgroundUpdateCheckPolicy.GetDelayUntilNextCheck(status));
    }

    [Fact]
    public void Crash_TakesTheShortRetryPath()
    {
        Assert.Equal(
            BackgroundUpdateCheckPolicy.FailureRetryInterval,
            BackgroundUpdateCheckPolicy.GetDelayUntilNextCheck(status: null));
    }
}

/// <summary>
/// Gate table for the background auto-download. The stakes: downloading on
/// a metered connection burns a cellular data plan; double-downloading
/// wastes the transfer and races two writers onto the same temp file.
/// </summary>
public sealed class UpdateAutoDownloadPolicyTests
{
    [Fact]
    public void HappyPath_DownloadsInBackground()
    {
        Assert.True(UpdateAutoDownloadPolicy.ShouldAutoDownload(
            autoDownloadEnabled: true,
            isDirectInstallerDelivery: true,
            isMeteredConnection: false,
            isDownloadInProgress: false,
            hasReadyInstaller: false));
    }

    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, false, false, true)]
    public void BlockedPaths_SkipTheDownload(
        bool enabled, bool direct, bool metered, bool inProgress, bool ready)
    {
        Assert.False(UpdateAutoDownloadPolicy.ShouldAutoDownload(
            enabled, direct, metered, inProgress, ready));
    }
}

/// <summary>
/// Decision table for the update status badge: the transient operations win
/// over the outcome states, and any waiting update (available or already
/// downloaded) reads as amber attention — the color that says "there is
/// something for you to do here", PowerToys' semantics.
/// </summary>
public sealed class UpdateStatusBadgePolicyTests
{
    [Fact]
    public void UpToDate_IsGreenSuccess()
    {
        Assert.Equal(
            UpdateStatusBadgeStates.Success,
            UpdateStatusBadgePolicy.GetStateName(
                isCheckingForUpdates: false,
                isDownloadingUpdate: false,
                lastDownloadFailed: false,
                hasUpdateManifest: false));
    }

    [Fact]
    public void UpdateAvailable_IsAmberAttention()
    {
        Assert.Equal(
            UpdateStatusBadgeStates.Attention,
            UpdateStatusBadgePolicy.GetStateName(
                isCheckingForUpdates: false,
                isDownloadingUpdate: false,
                lastDownloadFailed: false,
                hasUpdateManifest: true));
    }

    [Fact]
    public void CheckingAndDownloading_BeatOutcomeStates()
    {
        Assert.Equal(
            UpdateStatusBadgeStates.Checking,
            UpdateStatusBadgePolicy.GetStateName(
                isCheckingForUpdates: true,
                isDownloadingUpdate: false,
                lastDownloadFailed: true,
                hasUpdateManifest: true));
        Assert.Equal(
            UpdateStatusBadgeStates.Downloading,
            UpdateStatusBadgePolicy.GetStateName(
                isCheckingForUpdates: false,
                isDownloadingUpdate: true,
                lastDownloadFailed: true,
                hasUpdateManifest: true));
    }

    [Fact]
    public void DownloadFailure_IsError_UnlessWorkIsInFlight()
    {
        Assert.Equal(
            UpdateStatusBadgeStates.Error,
            UpdateStatusBadgePolicy.GetStateName(
                isCheckingForUpdates: false,
                isDownloadingUpdate: false,
                lastDownloadFailed: true,
                hasUpdateManifest: true));
    }
}
