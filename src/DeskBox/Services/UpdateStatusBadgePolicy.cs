namespace DeskBox.Services;

/// <summary>
/// State names shared between the update badge control and its string-typed
/// binding source. String-typed on purpose: the AOT bindable-property
/// provider marshals enums unreliably, and every other switch-style VM
/// surface in this codebase binds strings.
/// </summary>
public static class UpdateStatusBadgeStates
{
    public const string Success = "Success";
    public const string Checking = "Checking";
    public const string Attention = "Attention";
    public const string Downloading = "Downloading";
    public const string Error = "Error";
}

/// <summary>
/// Pure mapping from the update view-model's state flags to the badge state
/// (the PowerToys UpdateStateBadgeControl semantics): green check when
/// current, accent clock while checking, accent download while
/// downloading, red cross after a failed download, and amber attention
/// whenever an update is waiting for the user — available or already
/// downloaded and ready to install.
/// </summary>
public static class UpdateStatusBadgePolicy
{
    public static string GetStateName(
        bool isCheckingForUpdates,
        bool isDownloadingUpdate,
        bool lastDownloadFailed,
        bool hasUpdateManifest)
    {
        if (isCheckingForUpdates)
        {
            return UpdateStatusBadgeStates.Checking;
        }

        if (isDownloadingUpdate)
        {
            return UpdateStatusBadgeStates.Downloading;
        }

        if (lastDownloadFailed)
        {
            return UpdateStatusBadgeStates.Error;
        }

        return hasUpdateManifest
            ? UpdateStatusBadgeStates.Attention
            : UpdateStatusBadgeStates.Success;
    }
}
