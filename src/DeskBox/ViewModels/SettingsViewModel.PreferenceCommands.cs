using System.Globalization;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskBox.ViewModels;

public partial class SettingsViewModel
{
    public Color GetCurrentAccentColor() => _currentAccentColor;

    // Host-side working state for the accent card. The XAML binding surface
    // lives on the appearance editor (section-level DataContext switch); the
    // shell keeps the accent-mode flag its theme-service write chain needs
    // and pushes the accent presentation onto the editor on every change.
    internal bool UseSystemAccentColor
    {
        get => _useSystemAccentColor;
        private set => SetProperty(ref _useSystemAccentColor, value);
    }

    public bool SuppressAppearanceNotifications { get; set; }
    public bool DeferAppearancePersistence { get; set; }

    public void CommitAppearanceChanges()
    {
        _settingsService.NotifyAppearancePreviewNow();
        // The preview notification has already updated every widget with the
        // final slider value. Persist without broadcasting the same appearance
        // pass a second time through SettingsChanged.
        _settingsService.SaveDebounced(notifySubscribers: false);
        App.ScheduleLightMemoryCleanup();
    }

    public void SetCustomAccentColor(Color color)
    {
        _themeService.SetCustomAccentColor(color);

        if (UseSystemAccentColor)
        {
            UseSystemAccentColor = false;
        }

        RefreshAccentPreview();
    }

    public void UpdateManagedStorageRootPath(string path)
    {
        // The coordinator owns the raw-path normalization, the stored write
        // and the debounced save; the file migration that moved widget
        // content to the new root already ran on the host's existing
        // WidgetManager chain before this commit step. The editor owns the
        // section's read-only path display, so the committed path is pushed
        // onto its binding surface by the commit itself.
        string normalizedPath = _managedStorageSettings.CommitRootPath(path);
        ManagedStorageRootPath = normalizedPath;
        _ = RefreshQuickAccessStateAsync(showBusy: true);
    }

    public async Task RestoreDefaultPreferencesAsync()
    {
        _isRestoringDefaults = true;
        SuppressAppearanceNotifications = true;
        DeferAppearancePersistence = false;

        try
        {
            SettingsService.ApplyDefaultPreferences(_settingsService.Settings);
            ApplySettingsSnapshot();
            IconHelper.ClearAllThumbnailCaches();

            if (App.Current is { } app)
            {
                // ApplySettingsSnapshot above already re-projected the
                // restored snap state onto the interaction editor.
                app.ResizeGuideOverlay.IsSnapEnabled = _interactionSettings.SnapEnabled;
            }

            App.Current?.GlobalHotkeyService?.RefreshRegistration();
            App.Current?.UpdateTrayIcon();
            RefreshGlobalHotkeyState();
            _themeService.RefreshAppearance();
            RefreshAccentPreview();
            await _settingsService.SaveAsync();
            _quickCaptureSettings.RefreshFromSettings();
            _settingsService.NotifyAppearancePreviewNow();
        }
        finally
        {
            SuppressAppearanceNotifications = false;
            _isRestoringDefaults = false;
        }
    }

}
