using DeskBox.Models;
using DeskBox.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DeskBox.Controls;

public readonly record struct FileItemDragPackageResult(
    IReadOnlyList<string> SourcePaths,
    bool HasStorageItems,
    bool UsesNativeShellDataObject);

/// <summary>
/// Creates the common file-item drag payload. Hosts remain responsible for
/// deciding which items are dragged and how the completed drop is reconciled.
/// </summary>
public static class FileItemDragPackage
{
    internal const DataPackageOperation PreferredOperation =
        DataPackageOperation.Move;

    internal const DataPackageOperation SupportedOperations =
        DataPackageOperation.Copy |
        DataPackageOperation.Move |
        DataPackageOperation.Link;

    internal const DataPackageOperation ManagedShortcutSupportedOperations =
        DataPackageOperation.Move |
        DataPackageOperation.Link;

    internal static DataPackageOperation ResolveSupportedOperations(
        bool isManagedShortcutDrag) =>
        isManagedShortcutDrag
            ? ManagedShortcutSupportedOperations
            : SupportedOperations;

    /// <summary>
    /// ListViewBase item drags never forward DragStartingEventArgs.AllowedOperations
    /// to the underlying drag operation, so the operation mask advertised to
    /// external OLE drop targets is exactly RequestedOperation (None yields an
    /// undroppable drag). A lone Move makes every Copy-only target
    /// (Chromium/Electron drop zones, WM_DROPFILES games, WinForms) reject the
    /// drop. The full mask is therefore requested whenever the payload is a
    /// native Shell data object that hides the resulting multi-bit preferred
    /// drop effect from Explorer; the target then applies the standard
    /// same-volume-move / cross-volume-copy defaults. Managed shortcuts keep
    /// Move so dragging them back to the desktop restores instead of copies,
    /// and the StorageItems fallback keeps Move because its preferred effect
    /// cannot be hidden.
    /// Windows 10 stays on the single Move value: the hiding layer cannot
    /// reach Win10 Explorer (the multi-bit preference leaks and every plain
    /// drop prompts for an operation), so the wide mask is Win11-only until
    /// a self-driven DoDragDrop becomes viable (see the drag contract doc,
    /// 8.1.1b experiment log). The OS gate is injectable so tests can pin
    /// both branches regardless of the host they run on.
    /// </summary>
    internal static DataPackageOperation ResolveRequestedOperation(
        bool isManagedShortcutDrag,
        bool hidesPreferredDropEffect,
        bool? isWindows11OrLater = null) =>
        !isManagedShortcutDrag &&
            hidesPreferredDropEffect &&
            (isWindows11OrLater ??
                Services.WindowsCompatibilityService.IsWindows11OrLater)
            ? SupportedOperations
            : PreferredOperation;

    public static IReadOnlyList<WidgetItem> ResolveDraggedItems(
        IReadOnlyList<WidgetItem> eventItems,
        IReadOnlyList<WidgetItem> selectedItems)
    {
        WidgetItem[] distinctEventItems = eventItems.Distinct().ToArray();
        WidgetItem[] distinctSelectedItems = selectedItems.Distinct().ToArray();
        if (distinctSelectedItems.Length <= 1 || distinctEventItems.Length == 0)
        {
            return distinctEventItems;
        }

        // Some WinUI ListView input paths report only the pointer anchor in
        // DragItemsStarting even though it belongs to a larger selection. The
        // visible selection is authoritative whenever the event anchor is one
        // of its members.
        return distinctEventItems.Any(distinctSelectedItems.Contains)
            ? distinctSelectedItems
            : distinctEventItems;
    }

    public static bool TryPrepare(
        DataPackage dataPackage,
        IReadOnlyList<WidgetItem> draggedItems,
        string sourceWidgetId,
        Func<IEnumerable<string>, IReadOnlyList<IStorageItem>> getStorageItems,
        Func<IReadOnlyList<string>, string> getTitle,
        out FileItemDragPackageResult result,
        bool isManagedShortcutDrag = false)
    {
        result = default;
        if (draggedItems.Count == 0)
        {
            return false;
        }

        string[] sourcePaths = draggedItems
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            return false;
        }

        // Prefer a native Shell IDataObject for every file drag. It is what
        // Explorer itself produces, so external targets see the same formats
        // and Explorer owns the desktop drop position. It also sidesteps the
        // WinRT StorageFile broker, which can reject .lnk files (including
        // ones whose filesystem attributes look normal) and which this UI-STA
        // event would otherwise have to wait on synchronously. Regular files
        // additionally hide the preferred drop effect so the full operation
        // mask can be requested (see ResolveRequestedOperation).
        bool hidePreferredDropEffect = !isManagedShortcutDrag;
        bool usesNativeShellDataObject =
            NativeShellFileDragProvider.TryAttach(
                dataPackage,
                sourcePaths,
                hidePreferredDropEffect);
        IReadOnlyList<IStorageItem> storageItems = [];
        if (!usesNativeShellDataObject)
        {
            if (NativeShellFileDragProvider.RequiresStorageBrokerBypass(
                    sourcePaths))
            {
                App.Log(
                    $"[DragStart] Canceled broker-blocked file drag because " +
                    $"a native Shell payload could not be created paths=" +
                    $"{sourcePaths.Length}");
                return false;
            }

            // Never advertise a partial selection or fall back to a
            // coordinate-free filesystem move after Drop.
            storageItems = getStorageItems(sourcePaths);
            if (storageItems.Count != sourcePaths.Length)
            {
                App.Log(
                    $"[DragStart] Canceled file drag because only a " +
                    $"partial StorageItems payload was available " +
                    $"paths={sourcePaths.Length}");
                return false;
            }

            dataPackage.SetStorageItems(storageItems, readOnly: false);
        }

        // For ListViewBase item drags RequestedOperation is the external
        // allowed-operation mask. Multiple flags are only safe when the
        // resulting preferred drop effect is hidden from Explorer; otherwise
        // Windows 10 asks the user to choose an operation for every ordinary
        // left-button drop.
        dataPackage.RequestedOperation = ResolveRequestedOperation(
            isManagedShortcutDrag,
            hidesPreferredDropEffect:
                usesNativeShellDataObject && hidePreferredDropEffect);

        dataPackage.Properties[DeskBoxDragData.SourceWidgetIdProperty] =
            sourceWidgetId;
        dataPackage.Properties[DeskBoxDragData.SourcePathsProperty] =
            sourcePaths;
        dataPackage.Properties[
            DeskBoxDragData.InternalFileDragTokenProperty] =
            DeskBoxDragData.InternalFileDragToken;
        // No text representation: Chromium turns CF_UNICODETEXT paths into
        // text/plain plus text/uri-list, and Electron drop zones then treat
        // the drag as text or a link instead of files. Explorer offers none.
        dataPackage.Properties.Title = getTitle(sourcePaths);

        result = new FileItemDragPackageResult(
            sourcePaths,
            storageItems.Count > 0 || usesNativeShellDataObject,
            usesNativeShellDataObject);
        return true;
    }
}
