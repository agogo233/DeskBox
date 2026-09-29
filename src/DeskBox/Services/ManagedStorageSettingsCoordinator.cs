using DeskBox.Contracts;
using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Sole settings-page writer for the managed-storage section: the default
/// managed storage root path and the managed drop action
/// (<see cref="FileWidgetSettingsSlice"/>). The root-path write is the commit
/// step that follows the host's existing storage migration chain — the
/// WidgetManager moves widget content between roots with its own rollback,
/// residue cleanup and stored-path writes inside that chain; this coordinator
/// owns only the settings page's persisted write. Every write keeps the
/// original command semantics: normalize the raw value through the shared
/// SettingsService normalizers (blank or unusable paths fall back to the
/// default root; unknown drop actions fall back to Move), skip unchanged
/// writes, store, and schedule one debounced save with the regular
/// SettingsChanged broadcast. The settings window filters no-op path changes
/// before invoking the flow, so that write is unconditional, matching the
/// pre-migration command.
/// </summary>
public sealed class ManagedStorageSettingsCoordinator : IManagedStorageSettings
{
    private readonly SettingsService _settings;
    private bool _stopped;

    internal bool IsStopped => _stopped;

    public ManagedStorageSettingsCoordinator(SettingsService settings)
    {
        _settings = settings;
    }

    public ManagedStoragePresentationSettings ReadManagedStoragePresentation()
    {
        FileWidgetSettingsSlice fileWidget = _settings.Settings.FileWidget;
        return new ManagedStoragePresentationSettings(
            NormalizeDropAction(fileWidget.ManagedDropAction),
            SettingsService.NormalizeManagedStorageRootPath(
                fileWidget.DefaultManagedStorageRootPath));
    }

    public string ReadDefaultRootPath() =>
        _settings.Settings.FileWidget.DefaultManagedStorageRootPath;

    public string SetDefaultRootPath(string path)
    {
        ThrowIfStopped();
        string normalizedPath = SettingsService.NormalizeManagedStorageRootPath(path);
        _settings.Settings.FileWidget.DefaultManagedStorageRootPath = normalizedPath;
        _settings.SaveDebounced();
        return normalizedPath;
    }

    public bool SetManagedDropAction(string? action)
    {
        ThrowIfStopped();
        string normalized = NormalizeDropAction(action);
        FileWidgetSettingsSlice fileWidget = _settings.Settings.FileWidget;
        if (string.Equals(
                fileWidget.ManagedDropAction,
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        fileWidget.ManagedDropAction = normalized;
        _settings.SaveDebounced();
        return true;
    }

    private static string NormalizeDropAction(string? action) => action switch
    {
        ManagedDropActions.Copy => ManagedDropActions.Copy,
        ManagedDropActions.FollowWindows => ManagedDropActions.FollowWindows,
        _ => ManagedDropActions.Move
    };

    private void ThrowIfStopped() => ObjectDisposedException.ThrowIf(_stopped, this);

    internal void Stop() => _stopped = true;
}
