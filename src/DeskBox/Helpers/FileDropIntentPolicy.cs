using DeskBox.Platform;
using DeskBox.Services;

namespace DeskBox.Helpers;

internal enum FileDropIntent
{
    None,
    Copy,
    Move,
    Shortcut,
    Reference,
    Reorder,
    Organize
}

/// <summary>
/// Resolves the semantic intent of a file drop before individual XAML and OLE
/// adapters translate it into their platform-specific operation flags.
/// </summary>
internal static class FileDropIntentPolicy
{
    public static FileDropIntent ResolveMappedTransfer(
        bool hasMappedFolder,
        bool forceCopy,
        bool controlDown,
        bool shiftDown,
        bool defaultMove,
        bool canCopy = true,
        bool canMove = true,
        bool altDown = false,
        bool followWindows = false,
        bool sameVolume = true,
        bool canLink = true,
        bool shortcutOutsideDesktop = false,
        bool sourcesOnDesktop = true,
        bool protectDefaultMoveAsCopy = false,
        SourceOriginSnapshot? sourceOrigin = null,
        TransferDomain domain = TransferDomain.ExternalInbound)
    {
        // Explicit modifier gestures always win — keeping them ahead of the
        // shared engine preserves the local behaviour where Alt (or Ctrl+Shift)
        // drags from Explorer stay a link even when the source already lives
        // on the desktop, and Ctrl/Shift keep their copy/move meaning on
        // unmapped widgets too.
        if (forceCopy)
        {
            return canCopy ? FileDropIntent.Copy : FileDropIntent.None;
        }

        if ((altDown || (controlDown && shiftDown)) && canLink)
        {
            return FileDropIntent.Shortcut;
        }

        if (controlDown)
        {
            return canCopy ? FileDropIntent.Copy : FileDropIntent.None;
        }

        if (shiftDown)
        {
            return canMove ? FileDropIntent.Move : FileDropIntent.None;
        }

        // The "shortcut outside the desktop" default keeps files that already
        // live on the desktop moving into managed storage, while every other
        // source becomes a link so the original never leaves its location.
        // Explicit modifier gestures above always take precedence. A caller
        // that cannot extract the source paths treats the payload as
        // desktop-resident to preserve the previous behaviour.
        if (shortcutOutsideDesktop)
        {
            if (sourcesOnDesktop && canMove)
            {
                return FileDropIntent.Move;
            }

            if (!sourcesOnDesktop && canLink)
            {
                return FileDropIntent.Shortcut;
            }
        }

        uint allowedEffects =
            (canCopy ? NativeDropEffectPolicy.Copy : 0u) |
            (canMove ? NativeDropEffectPolicy.Move : 0u) |
            (canLink ? NativeDropEffectPolicy.Link : 0u);
        SourceOriginSnapshot origin = sourceOrigin ?? new SourceOriginSnapshot(
            protectDefaultMoveAsCopy
                ? InboundSourceProtectionKind.ExternalRemovableOrNetwork
                : InboundSourceProtectionKind.None,
            HasMoveProtectedRoot: !canMove,
            HasUnhydratedCloudPlaceholder: false,
            HasApplicationCacheSource: false,
            HasTransientTempOrMsixCacheSource: false);
        return TransferIntentEngine.Evaluate(new TransferIntentRequest(
            Domain: domain,
            TargetKind: TransferTargetKind.DirectorySurface,
            HasFileData: true,
            HasVirtualFileData: false,
            HasShellApplicationOrNamespaceData: false,
            ForceCopy: forceCopy,
            HasMappedFolder: hasMappedFolder,
            ControlDown: controlDown,
            ShiftDown: shiftDown,
            AltDown: altDown,
            AllowedEffects: allowedEffects,
            NoOperationMetadata: false,
            DefaultMove: defaultMove,
            FollowWindows: followWindows,
            SameVolume: sameVolume,
            CanLink: canLink,
            SourceOrigin: origin)).Intent;
    }

    public static bool AreSameVolume(string sourcePath, string destinationPath) =>
        PathOriginClassifier.AreSameVolume(sourcePath, destinationPath);

    public static bool AreAllOnSameVolume(
        IEnumerable<string> sourcePaths,
        string destinationPath) =>
        PathOriginClassifier.AreAllOnSameVolume(sourcePaths, destinationPath);

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="directory"/> itself
    /// or one of its descendants. Comparison is case-insensitive and guarded
    /// against sibling prefixes such as "DesktopBackup".
    /// </summary>
    public static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (fullRoot.Length == 0)
            {
                return false;
            }

            return string.Equals(
                    fullPath,
                    fullRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(
                    fullRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Invalid or inaccessible paths are never treated as resident.
            return false;
        }
    }

    /// <summary>
    /// Whether every non-empty <paramref name="sourcePaths"/> entry lives under
    /// at least one of the given <paramref name="directories"/>. An unknown
    /// payload (no extractable root) resolves as desktop-resident so the
    /// default move behaviour is preserved rather than surprising the user.
    /// </summary>
    public static bool AreAllUnderDirectories(
        IEnumerable<string> sourcePaths,
        IEnumerable<string> directories)
    {
        try
        {
            string[] roots = directories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(directory => Path.GetFullPath(directory))
                .ToArray();
            if (roots.Length == 0)
            {
                return true;
            }

            return sourcePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .All(path => roots.Any(root => IsUnderDirectory(path, root)));
        }
        catch
        {
            // Invalid or inaccessible roots fall back to desktop-resident so
            // the existing move behaviour is preserved.
            return true;
        }
    }
}
