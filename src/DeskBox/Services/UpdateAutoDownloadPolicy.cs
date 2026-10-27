namespace DeskBox.Services;

/// <summary>
/// Pure gate for the background auto-download trigger (the PowerToys
/// downloadUpdatesAutomatically semantics): user opt-in, direct channel
/// only (Store installs flow through the Store UI), never on a metered
/// connection, never twice at once, and never when an installer for this
/// update is already sitting on disk.
/// </summary>
public static class UpdateAutoDownloadPolicy
{
    public static bool ShouldAutoDownload(
        bool autoDownloadEnabled,
        bool isDirectInstallerDelivery,
        bool isMeteredConnection,
        bool isDownloadInProgress,
        bool hasReadyInstaller)
    {
        return autoDownloadEnabled &&
            isDirectInstallerDelivery &&
            !isMeteredConnection &&
            !isDownloadInProgress &&
            !hasReadyInstaller;
    }
}
