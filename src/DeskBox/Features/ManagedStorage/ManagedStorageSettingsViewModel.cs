using CommunityToolkit.Mvvm.ComponentModel;
using DeskBox.Contracts;
using DeskBox.Models;

namespace DeskBox.Features.ManagedStorage;

/// <summary>
/// Managed-storage section settings editor. Owns the section's XAML binding
/// surface: the managed drop action (combo), the read-only default root path
/// display and the quick-access card presentation (status line, pin/unpin
/// button face and tooltip, action gate). The drop action reads project
/// through the read snapshot of <see cref="IManagedStorageSettings"/> and
/// user edits write through the same coordinator port the legacy shell used;
/// external refresh paths (settings broadcasts, default restores) re-sync the
/// projection instead of writing back. The root path and the quick-access
/// presentation are pushed by the shell: the folder picker, the storage
/// migration chain and the Explorer quick-access state machine stay on the
/// host because they reach host services. The section-level DataContext
/// switch means the property names no longer need to be unique across the
/// whole shell; the drop action drops its legacy prefix and the path its
/// storage qualifier, keeping clear of the flat facade-name ratchet. Like the
/// music editor, this class references neither App nor WinUI nor the settings
/// adapter; localization arrives as a delegate.
/// </summary>
public sealed partial class ManagedStorageSettingsViewModel : ObservableObject
{
    private static readonly string[] DropActions =
    [
        ManagedDropActions.Copy,
        ManagedDropActions.Move,
        ManagedDropActions.FollowWindows
    ];

    private static readonly string[] DropActionNameKeys =
    [
        "Settings.DropAction.Copy",
        "Settings.DropAction.Move",
        "Settings.DropAction.System"
    ];

    private readonly IManagedStorageSettings _settings;
    private readonly Func<string, string> _localize;
    private bool _isSyncingPresentation;
    private string[]? _cachedDropActionNames;
    private string _dropAction = ManagedDropActions.Move;
    private string _rootPath = string.Empty;
    private bool _canInvokeQuickAccessAction = true;
    private bool _shouldUnpinQuickAccessAction;
    private string _quickAccessStatusText = string.Empty;
    private string _pinQuickAccessButtonText = string.Empty;
    private string _pinQuickAccessToolTipText = string.Empty;

    public ManagedStorageSettingsViewModel(
        IManagedStorageSettings settings,
        Func<string, string> localize)
    {
        _settings = settings;
        _localize = localize;
        SyncPresentation();
    }

    public string DropAction
    {
        get => _dropAction;
        set
        {
            // The combo only offers canonical values and the coordinator
            // normalizes writes, so the editor stores the raw selection and
            // re-syncs from the normalized snapshot on the next broadcast.
            if (!SetProperty(ref _dropAction, value))
            {
                return;
            }

            if (!_isSyncingPresentation)
            {
                _settings.SetManagedDropAction(value);
            }
        }
    }

    public IReadOnlyList<SettingsOption> AvailableDropActionOptions
    {
        get
        {
            // Build a real SettingsOption[] (not a collection expression): the
            // hidden read-only-array type cannot marshal across the WinRT ABI
            // in Native AOT builds and would leave the ItemsSource empty.
            _cachedDropActionNames ??= DropActionNameKeys.Select(key => _localize(key)).ToArray();
            var options = new SettingsOption[DropActions.Length];
            for (int index = 0; index < DropActions.Length; index++)
            {
                options[index] = new SettingsOption(DropActions[index], _cachedDropActionNames[index]);
            }

            return options;
        }
    }

    /// <summary>
    /// Pushed by the shell: the normalized default root path display. The
    /// folder picker and the migration commit chain stay on the host.
    /// </summary>
    public string RootPath
    {
        get => _rootPath;
        private set => SetProperty(ref _rootPath, value);
    }

    /// <summary>
    /// Pushed by the shell: whether the pin/unpin action can run right now.
    /// </summary>
    public bool CanInvokeQuickAccessAction
    {
        get => _canInvokeQuickAccessAction;
        private set => SetProperty(ref _canInvokeQuickAccessAction, value);
    }

    /// <summary>
    /// Pushed by the shell: whether the pinned state means the next action
    /// unpins. Consumed by the code-behind action handler.
    /// </summary>
    public bool ShouldUnpinQuickAccessAction
    {
        get => _shouldUnpinQuickAccessAction;
        private set => SetProperty(ref _shouldUnpinQuickAccessAction, value);
    }

    /// <summary>Pushed by the shell: the quick-access status line.</summary>
    public string QuickAccessStatusText
    {
        get => _quickAccessStatusText;
        private set => SetProperty(ref _quickAccessStatusText, value);
    }

    /// <summary>Pushed by the shell: the pin/unpin button face.</summary>
    public string PinQuickAccessButtonText
    {
        get => _pinQuickAccessButtonText;
        private set => SetProperty(ref _pinQuickAccessButtonText, value);
    }

    /// <summary>Pushed by the shell: the pin/unpin button tooltip.</summary>
    public string PinQuickAccessToolTipText
    {
        get => _pinQuickAccessToolTipText;
        private set => SetProperty(ref _pinQuickAccessToolTipText, value);
    }

    /// <summary>
    /// Re-projects the persisted managed-storage presentation (drop action
    /// and root path) onto the binding surface without writing back. Called
    /// on construction, settings broadcasts and default restores.
    /// </summary>
    public void SyncPresentation()
    {
        ManagedStoragePresentationSettings snapshot =
            _settings.ReadManagedStoragePresentation();
        _isSyncingPresentation = true;
        try
        {
            DropAction = snapshot.DropAction;
            RootPath = snapshot.RootPath;
        }
        finally
        {
            _isSyncingPresentation = false;
        }
    }

    /// <summary>
    /// Drops the localized drop-action name cache after a language change so
    /// the options list re-projects in the new language.
    /// </summary>
    public void RefreshLocalization()
    {
        _cachedDropActionNames = null;
        OnPropertyChanged(nameof(AvailableDropActionOptions));
    }

    /// <summary>
    /// Commits a new default root path through the coordinator (normalize,
    /// store, one debounced save) and re-projects it onto the binding
    /// surface. Called by the shell's migration commit step; the migration
    /// chain itself stays on the host.
    /// </summary>
    public string CommitRootPath(string path)
    {
        string normalizedPath = _settings.SetDefaultRootPath(path);
        UpdateRootPath(normalizedPath);
        return normalizedPath;
    }

    /// <summary>
    /// Re-projects the default root path the shell committed after its
    /// folder picker and migration chain finished.
    /// </summary>
    public void UpdateRootPath(string rootPath)
    {
        RootPath = rootPath;
    }

    /// <summary>
    /// Re-projects the quick-access card presentation the shell computed from
    /// its Explorer quick-access state machine. Does not write anything back.
    /// </summary>
    public void UpdateQuickAccessPresentation(QuickAccessPresentationSettings presentation)
    {
        CanInvokeQuickAccessAction = presentation.CanInvoke;
        ShouldUnpinQuickAccessAction = presentation.ShouldUnpin;
        QuickAccessStatusText = presentation.StatusText;
        PinQuickAccessButtonText = presentation.ButtonText;
        PinQuickAccessToolTipText = presentation.ToolTipText;
    }
}
