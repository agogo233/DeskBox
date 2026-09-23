using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views;

public sealed partial class OnboardingWindow
{
    private const string StatusInfoGlyph = "\uE946";
    private const string StatusCompleteGlyph = "\uE73E";
    private const string StatusHiddenGlyph = "\uE890";
    private const string StatusVisibleGlyph = "\uE8A7";

    private bool _isFilePracticeWidgetRaised;
    private bool _hasCompletedFilePractice;
    private bool _hasHiddenWidgetsDuringPractice;
    private bool _hasCompletedVisibilityPractice;
    private bool _isSynchronizingTaskStorageEntryToggles;
    private int _storageEntryStateRefreshGeneration;

    // Quick Access enumeration goes through Explorer's shell namespace and can
    // stall for a long time (disconnected shares, stale Recent items, busy
    // explorer.exe). Never wait on it from the UI thread, and stop waiting
    // after this long so the guide stays interactive.
    private static readonly TimeSpan StorageEntryStateQueryTimeout = TimeSpan.FromSeconds(8);

    private void SetupTaskStep1()
    {
    }

    private void SetupTaskStep2()
    {
    }

    private void SetupTaskStep3()
    {
        RefreshTaskStep3StorageEntryState();
        SetTaskStep3Status(
            _hasCompletedFilePractice
                ? "Onboarding.Task.Step3.StatusCompleted"
                : "Onboarding.Task.Step3.StatusReady",
            _hasCompletedFilePractice ? StatusCompleteGlyph : StatusInfoGlyph);

        if (!_hasCompletedFilePractice && !_isFilePracticeWidgetRaised)
        {
            _ = ShowFilePracticeWidgetAsync();
        }
    }

    private void RefreshTaskStep3StorageEntryState()
    {
        string storagePath = SettingsService.NormalizeManagedStorageRootPath(
            _settingsService.Settings.DefaultManagedStorageRootPath);
        TaskStep3StoragePathText.Text = storagePath;
        _ = RefreshTaskStep3StorageEntryStateAsync(storagePath);
    }

    private async Task RefreshTaskStep3StorageEntryStateAsync(string storagePath)
    {
        int generation = ++_storageEntryStateRefreshGeneration;
        TaskStep3QuickAccessToggle.IsEnabled = false;
        TaskStep3DesktopShortcutToggle.IsEnabled = false;

        ManagedStorageDesktopShortcutService shortcutService =
            global::DeskBox.App.Current.ManagedStorageDesktopShortcutService;
        Task<QuickAccessStateResult> pinStateTask = ExplorerQuickAccessHelper
            .GetQuickAccessPinStateAsync(storagePath)
            .WaitAsync(StorageEntryStateQueryTimeout);
        Task<bool> shortcutTask = Task.Run(shortcutService.HasShortcut)
            .WaitAsync(StorageEntryStateQueryTimeout);

        bool isPinned = false;
        try
        {
            isPinned = (await pinStateTask).State == QuickAccessPinState.Pinned;
        }
        catch (Exception ex)
        {
            App.Log($"[Onboarding] Quick Access pin state query failed: {ex.Message}");
        }

        bool hasShortcut = false;
        try
        {
            hasShortcut = await shortcutTask;
        }
        catch (Exception ex)
        {
            App.Log($"[Onboarding] Desktop shortcut state query failed: {ex.Message}");
        }

        if (_isClosed || generation != _storageEntryStateRefreshGeneration)
        {
            return;
        }

        _isSynchronizingTaskStorageEntryToggles = true;
        try
        {
            TaskStep3QuickAccessToggle.IsOn = isPinned;
            TaskStep3DesktopShortcutToggle.IsOn = hasShortcut;
        }
        finally
        {
            _isSynchronizingTaskStorageEntryToggles = false;
        }

        TaskStep3QuickAccessToggle.IsEnabled = true;
        TaskStep3DesktopShortcutToggle.IsEnabled = true;
    }

    private async void TaskStep3QuickAccessToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizingTaskStorageEntryToggles ||
            sender is not ToggleSwitch toggle)
        {
            return;
        }

        bool requestedState = toggle.IsOn;
        toggle.IsEnabled = false;
        try
        {
            string storagePath = SettingsService.NormalizeManagedStorageRootPath(
                _settingsService.Settings.DefaultManagedStorageRootPath);
            QuickAccessOperationResult result = requestedState
                ? await ExplorerQuickAccessHelper.TryPinFolderToQuickAccessAsync(storagePath)
                : await ExplorerQuickAccessHelper.TryUnpinFolderFromQuickAccessAsync(storagePath);
            if (result.Succeeded)
            {
                return;
            }

            SetTaskStorageToggleState(toggle, !requestedState);
            await ShowTaskStorageEntryErrorAsync(
                "Onboarding.Step4.PinTitle",
                requestedState
                    ? "Onboarding.Step4.PinFailedBody"
                    : "Common.OperationFailedRetry");
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private async void TaskStep3DesktopShortcutToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizingTaskStorageEntryToggles ||
            sender is not ToggleSwitch toggle)
        {
            return;
        }

        bool requestedState = toggle.IsOn;
        toggle.IsEnabled = false;
        try
        {
            ManagedStorageDesktopShortcutService shortcutService =
                global::DeskBox.App.Current.ManagedStorageDesktopShortcutService;
            bool succeeded = requestedState
                ? await shortcutService.CreateAsync()
                : await shortcutService.RemoveAsync();
            if (succeeded)
            {
                return;
            }

            SetTaskStorageToggleState(toggle, !requestedState);
            await ShowTaskStorageEntryErrorAsync(
                "Settings.ManagedPath.DesktopShortcut.Title",
                "Common.OperationFailedRetry");
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private void SetTaskStorageToggleState(ToggleSwitch toggle, bool isOn)
    {
        _isSynchronizingTaskStorageEntryToggles = true;
        try
        {
            toggle.IsOn = isOn;
        }
        finally
        {
            _isSynchronizingTaskStorageEntryToggles = false;
        }
    }

    private async Task ShowTaskStorageEntryErrorAsync(
        string titleKey,
        string bodyKey)
    {
        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = _localizationService.T(titleKey),
            Content = new TextBlock
            {
                Text = _localizationService.T(bodyKey),
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = _localizationService.T("Common.Ok"),
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private void SetupTaskStep4()
    {
        string hotkeyText = GlobalHotkeyService.FormatActivation(
            GlobalHotkeyService.NormalizeActivation(
                _settingsService.Settings.GlobalHotkeyActivationKind,
                _settingsService.Settings.GlobalHotkeyModifiers,
                _settingsService.Settings.GlobalHotkeyKey),
            _localizationService);
        TaskStep4HotkeyText.Text = _localizationService.Format(
            "Onboarding.Task.Step4.ToggleBody",
            hotkeyText);
        TaskStep4ShortcutText.Text = hotkeyText;
        if (!_hasCompletedVisibilityPractice &&
            global::DeskBox.App.Current.HasVisibleWidgetsForOnboarding == false)
        {
            _hasHiddenWidgetsDuringPractice = true;
        }

        SetTaskStep4Status(
            _hasCompletedVisibilityPractice
                ? "Onboarding.Task.Step4.StatusCompleted"
                : _hasHiddenWidgetsDuringPractice
                    ? "Onboarding.Task.Step4.StatusHidden"
                    : "Onboarding.Task.Step4.StatusReady",
            _hasCompletedVisibilityPractice
                ? StatusCompleteGlyph
                : _hasHiddenWidgetsDuringPractice
                    ? StatusHiddenGlyph
                    : StatusInfoGlyph);
        TaskStep4ToggleButton.Content = _localizationService.T(
            _hasHiddenWidgetsDuringPractice && !_hasCompletedVisibilityPractice
                ? "Tray.ShowAll"
                : "Onboarding.Task.Step4.ToggleButton");
        TaskStep4ToggleButton.Visibility = _hasCompletedVisibilityPractice
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateVisibilityPreview(global::DeskBox.App.Current.HasVisibleWidgetsForOnboarding);
    }

    private void SetupTaskStep5()
    {
        if (_hasInitializedFeatureToggles)
        {
            return;
        }

        SynchronizeFeatureTogglesFromSettings();
    }

    private void TaskStep5FeatureToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_hasInitializedFeatureToggles ||
            _isSynchronizingFeatureToggles ||
            sender is not ToggleSwitch { Tag: string kindName } toggle ||
            !Enum.TryParse(kindName, ignoreCase: false, out WidgetKind kind) ||
            !FeatureWidgetSettings.IsFeatureWidget(kind))
        {
            return;
        }

        _featureWidgetSelectionUpdateTask = PersistFeatureWidgetSelectionAfterAsync(
            _featureWidgetSelectionUpdateTask,
            kind,
            toggle.IsOn);
    }

    private async Task PersistFeatureWidgetSelectionAfterAsync(
        Task previousUpdate,
        WidgetKind kind,
        bool enabled)
    {
        try
        {
            await previousUpdate;

            if (global::DeskBox.App.Current.WidgetManager is { } widgetManager)
            {
                await widgetManager.SetFeatureWidgetEnabledAsync(
                    kind,
                    enabled,
                    reveal: enabled);
                return;
            }

            FeatureWidgetSettings.SetEnabled(_settingsService.Settings, kind, enabled);
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[Onboarding] Failed to persist feature selection kind={kind} enabled={enabled}: {ex}");
            FeatureWidgetSettings.SetEnabled(_settingsService.Settings, kind, enabled);
            await _settingsService.SaveAsync();
        }
    }

    private void OnFeatureWidgetSettingsChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(OnFeatureWidgetSettingsChanged);
            return;
        }

        if (_hasInitializedFeatureToggles)
        {
            SynchronizeFeatureTogglesFromSettings();
        }
    }

    private void SynchronizeFeatureTogglesFromSettings()
    {
        _isSynchronizingFeatureToggles = true;
        try
        {
            TaskStep5TodoToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.Todo);
            TaskStep5QuickCaptureToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.QuickCapture);
            TaskStep5SearchToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.Search);
            TaskStep5WeatherToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.Weather);
            TaskStep5MusicToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.Music);
            TaskStep5GlanceToggle.IsOn = FeatureWidgetSettings.IsEnabled(
                _settingsService.Settings,
                WidgetKind.Glance);
            _hasInitializedFeatureToggles = true;
        }
        finally
        {
            _isSynchronizingFeatureToggles = false;
        }
    }

    private async Task ShowFilePracticeWidgetAsync()
    {
        _isFilePracticeWidgetRaised = true;
        bool shown = await global::DeskBox.App.Current.ShowFirstFileWidgetForOnboardingAsync();
        if (!shown)
        {
            _isFilePracticeWidgetRaised = false;
        }

        if (_stepIndex == 0 && !_hasCompletedFilePractice)
        {
            SetTaskStep3Status(
                shown
                    ? "Onboarding.Task.Step3.StatusShown"
                    : "Onboarding.Task.Step2.StatusUnavailable",
                shown ? StatusVisibleGlyph : StatusInfoGlyph);
        }
    }

    private void ReleaseFilePracticeWidget()
    {
        if (!_isFilePracticeWidgetRaised)
        {
            return;
        }

        _isFilePracticeWidgetRaised = false;
        global::DeskBox.App.Current.ReleaseOnboardingFileWidgetRaise();
    }

    private async void TaskStep4ToggleWidgets_Click(object sender, RoutedEventArgs e)
    {
        TaskStep4ToggleButton.IsEnabled = false;
        await global::DeskBox.App.Current.ToggleWidgetsForOnboardingAsync();
        TaskStep4ToggleButton.IsEnabled = true;
    }

    private void TaskStep4OpenTrayMenu_Click(object sender, RoutedEventArgs e)
    {
        global::DeskBox.App.Current.ShowTrayContextMenuForOnboarding();
    }

    private void OnOnboardingFileImportCompleted(int importedItemCount)
    {
        if (_stepIndex != 0 || importedItemCount <= 0)
        {
            return;
        }

        _hasCompletedFilePractice = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            SetTaskStep3Status("Onboarding.Task.Step3.StatusCompleted", StatusCompleteGlyph);
            UpdateFooterState();
        });
    }

    private void OnOnboardingWidgetsVisibilityChanged(bool hasVisibleWidgets)
    {
        if (_stepIndex != 1)
        {
            return;
        }

        if (!hasVisibleWidgets)
        {
            _hasHiddenWidgetsDuringPractice = true;
        }
        else if (_hasHiddenWidgetsDuringPractice)
        {
            _hasCompletedVisibilityPractice = true;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            string statusKey = _hasCompletedVisibilityPractice
                ? "Onboarding.Task.Step4.StatusCompleted"
                : hasVisibleWidgets
                    ? "Onboarding.Task.Step4.StatusShown"
                    : "Onboarding.Task.Step4.StatusHidden";
            SetTaskStep4Status(
                statusKey,
                _hasCompletedVisibilityPractice
                    ? StatusCompleteGlyph
                    : hasVisibleWidgets
                        ? StatusVisibleGlyph
                        : StatusHiddenGlyph);
            TaskStep4ToggleButton.Content = _localizationService.T(
                _hasHiddenWidgetsDuringPractice && !_hasCompletedVisibilityPractice
                    ? "Tray.ShowAll"
                    : "Onboarding.Task.Step4.ToggleButton");
            TaskStep4ToggleButton.Visibility = _hasCompletedVisibilityPractice
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateVisibilityPreview(hasVisibleWidgets);
            UpdateFooterState();
        });
    }

    private void SetTaskStep3Status(string localizationKey, string glyph)
    {
        TaskStep3StatusIcon.Glyph = glyph;
        TaskStep3StatusText.Text = _localizationService.T(localizationKey);
        AnimateStatusFeedback(TaskStep3StatusBadge);
    }

    private void SetTaskStep4Status(string localizationKey, string glyph)
    {
        TaskStep4StatusIcon.Glyph = glyph;
        TaskStep4StatusText.Text = _localizationService.T(localizationKey);
        AnimateStatusFeedback(TaskStep4StatusBadge);
    }

    private void UpdateVisibilityPreview(bool hasVisibleWidgets)
    {
        _stepAmbientStoryboard?.Stop();
        var transform = GetElementTransform(TaskStep4PreviewWidgets);
        transform.TranslateY = hasVisibleWidgets ? 0 : 14;
        TaskStep4PreviewWidgets.Opacity = hasVisibleWidgets ? 1 : 0.22;
        TaskStep4HotkeyHalo.Opacity = hasVisibleWidgets ? 0.18 : 0.34;
        if (hasVisibleWidgets && _stepIndex == 1)
        {
            StartStepAmbientAnimation(1);
        }
    }

}
