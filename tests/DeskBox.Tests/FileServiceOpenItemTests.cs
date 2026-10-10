using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Folder-shortcut dispatch (#459): a shortcut whose target is a local
/// directory must be opened with the Shell's default verb — the desktop
/// double-click dispatch — so third-party file managers registered as the
/// Folder default handler take over. Every other kind keeps the existing
/// explorer-first "open" pipeline, and the #339 search-popup boundary stays
/// untouched.
/// </summary>
public sealed class FileServiceOpenItemTests
{
    [Fact]
    public void SelectOpenDispatchMode_OnlyFolderShortcutsUseTheDefaultVerb()
    {
        // The one new branch: a shortcut to an existing local directory.
        Assert.Equal(
            OpenItemDispatchMode.LocalDefaultVerb,
            FileService.SelectOpenDispatchMode(
                isShortcut: true,
                targetKind: ShortcutTargetKind.LocalFileSystem,
                targetIsDirectory: true));

        // File shortcuts keep the explicit-"open" pipeline.
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(
                true,
                ShortcutTargetKind.LocalFileSystem,
                false));

        // UNC, network and URI/shell-namespace targets stay with Explorer.
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(true, ShortcutTargetKind.Unc, true));
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(true, ShortcutTargetKind.NetworkDrive, true));
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(true, ShortcutTargetKind.UriOrShellNamespace, true));
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(true, ShortcutTargetKind.Unknown, true));

        // Plain folder entries are the #339 scope, not #459: unchanged.
        Assert.Equal(
            OpenItemDispatchMode.ShellDispatch,
            FileService.SelectOpenDispatchMode(
                false,
                ShortcutTargetKind.LocalFileSystem,
                true));
    }

    [Fact]
    public void ShortcutTargetProbe_ReportsDirectoryTargets()
    {
        string shortcutPath = Path.Combine(
            Path.GetTempPath(),
            $"DeskBox.Tests-{Guid.NewGuid():N}.lnk");
        string targetDirectory = Path.Combine(
            Path.GetTempPath(),
            $"DeskBox.Tests-{Guid.NewGuid():N}.dir");
        string targetFile = Path.Combine(
            Path.GetTempPath(),
            $"DeskBox.Tests-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(shortcutPath, "placeholder");
        File.WriteAllText(targetFile, "placeholder");
        try
        {
            ShortcutTargetProbeResult directoryResult = ShortcutTargetProbe.Probe(
                shortcutPath,
                targetDirectory);
            Assert.Equal(ShortcutTargetKind.LocalFileSystem, directoryResult.Kind);
            Assert.Equal(ShortcutTargetStatus.Existing, directoryResult.Status);
            Assert.True(directoryResult.TargetIsDirectory);

            ShortcutTargetProbeResult fileResult = ShortcutTargetProbe.Probe(
                shortcutPath,
                targetFile);
            Assert.Equal(ShortcutTargetKind.LocalFileSystem, fileResult.Kind);
            Assert.Equal(ShortcutTargetStatus.Existing, fileResult.Status);
            Assert.False(fileResult.TargetIsDirectory);
        }
        finally
        {
            File.Delete(shortcutPath);
            File.Delete(targetFile);
            Directory.Delete(targetDirectory);
        }
    }

    [Fact]
    public void OpenItemCore_DispatchesFolderShortcutsThroughTheDefaultVerbBranch()
    {
        string openItem = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/FileService.OpenItem.cs"));

        Assert.Contains(
            "OpenItemDispatchMode.LocalDefaultVerb",
            openItem,
            StringComparison.Ordinal);
        Assert.Contains(
            "Win32Helper.OpenWithDefaultVerbLocally(",
            openItem,
            StringComparison.Ordinal);
        // The legacy pipeline stays for everything else.
        Assert.Contains(
            "Win32Helper.OpenFile(ownerHwnd, pathToOpen)",
            openItem,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultVerbLaunch_ReusesTheLocalPipelineWithoutTheOpenVerbOrExplorer()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Platform/Win32Helper.cs"));
        string method = Slice(
            source,
            "internal static bool OpenWithDefaultVerbLocally",
            "private static IDisposable? SuppressElectronRunAsNodeForChildLaunch");

        Assert.DoesNotContain("Verb = \"open\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ExplorerShellLaunchService", method, StringComparison.Ordinal);
        // The desktop double-click dispatch keeps the local pipeline: the
        // ELECTRON_RUN_AS_NODE scrub and the pending observation must apply
        // here exactly as they do for the explicit-"open" local fallback.
        Assert.Contains(
            "SuppressElectronRunAsNodeForChildLaunch();",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "WatchPendingLocalShellExecute(path);",
            method,
            StringComparison.Ordinal);

        string fallback = Slice(
            source,
            "public static bool OpenFileOrChooseApp",
            "internal static string ResolveShellLaunchDirectory");
        Assert.Contains(
            "SuppressElectronRunAsNodeForChildLaunch();",
            fallback,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Pins the #339 boundary: the search popup's hard-coded explorer.exe
    /// dispatch for folders is a separate issue and must not move in #459's
    /// wake.
    /// </summary>
    [Fact]
    public void SearchPopupFolderOpen_StillUsesExplorerForNow()
    {
        string search = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/SearchPopupViewModel.cs"));

        Assert.Contains(
            """System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");""",
            search,
            StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }
}
