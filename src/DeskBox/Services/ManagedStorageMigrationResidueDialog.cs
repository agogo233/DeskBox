using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Services;

/// <summary>
/// Dialogs for the storage-migration leftover flows: a completed migration
/// whose old-root source folders could not be cleaned up, and a retry that
/// found the previous attempt's complete destination copy still in place.
/// Every destructive action offered here goes to the recycle bin.
/// </summary>
internal static class ManagedStorageMigrationResidueDialog
{
    /// <summary>
    /// Reports a completed migration with leftover old-root folders and
    /// offers to recycle them. Never deletes without the explicit choice.
    /// </summary>
    public static async Task ShowMigrationResidueAsync(
        XamlRoot xamlRoot,
        LocalizationService localizationService,
        Func<IEnumerable<string>, Task<int>> recycleFoldersAsync,
        ManagedStorageMigrationResult result)
    {
        IReadOnlyList<ManagedStorageMigrationResidue> residues = result.Residues;
        if (residues.Count == 0)
        {
            return;
        }

        string folderList = string.Join(
            Environment.NewLine,
            residues
                .Select(residue => Path.GetFileName(residue.SourceFolder.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)))
                .ToList());
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localizationService.T("Settings.Dialog.MigrateResidueTitle"),
            PrimaryButtonText = localizationService.T("Settings.Dialog.MigrateResidueCleanButton"),
            CloseButtonText = localizationService.T("Settings.Dialog.MigrateResidueKeepButton"),
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = localizationService.Format(
                        "Settings.Dialog.MigrateResidueBody",
                        residues.Count,
                        result.OldRootPath) +
                    Environment.NewLine +
                    folderList,
                TextWrapping = TextWrapping.Wrap
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        int recycledCount = await recycleFoldersAsync(
            residues.Select(residue => residue.SourceFolder));
        if (recycledCount == residues.Count)
        {
            return;
        }

        var failureDialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localizationService.T("Settings.Dialog.MigrateResidueCleanFailedTitle"),
            CloseButtonText = localizationService.T("Common.Ok"),
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = localizationService.Format(
                    "Settings.Dialog.MigrateResidueCleanFailedBody",
                    residues.Count - recycledCount),
                TextWrapping = TextWrapping.Wrap
            }
        };
        await failureDialog.ShowAsync();
    }

    /// <summary>
    /// Warns that the destination still holds the previous attempt's
    /// complete copy and offers to recycle it before retrying.
    /// </summary>
    public static async Task<ContentDialogResult> ShowStaleDestinationAsync(
        XamlRoot xamlRoot,
        LocalizationService localizationService,
        IReadOnlyList<string> staleDestinationFolders)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localizationService.T("Settings.Dialog.MigrateStaleTitle"),
            PrimaryButtonText = localizationService.T("Settings.Dialog.MigrateStaleCleanButton"),
            CloseButtonText = localizationService.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBlock
            {
                Text = localizationService.Format(
                        "Settings.Dialog.MigrateStaleBody",
                        staleDestinationFolders.Count) +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        staleDestinationFolders
                            .Select(path => Path.GetFileName(path.TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar)))
                            .ToList()),
                TextWrapping = TextWrapping.Wrap
            }
        };

        return await dialog.ShowAsync();
    }
}
