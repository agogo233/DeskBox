using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// The migration dialog's per-item decision policy. A sticky "apply to
/// all" answer must never be Retry: it feeds FileService's per-item retry
/// loop, so a locked file would auto-retry forever without re-asking.
/// </summary>
public sealed class ManagedStorageMigrationDialogTests
{
    [Fact]
    public void StickyItemAction_SkipAppliesToAll()
    {
        Assert.Equal(
            FileService.FileTransferItemAction.Skip,
            ManagedStorageMigrationDialog.ResolveStickyItemAction(
                applyToAll: true,
                FileService.FileTransferItemAction.Skip));
    }

    [Fact]
    public void StickyItemAction_RetryNeverSticks()
    {
        // Apply-all + Retry means "retry this one", not "retry forever":
        // the next failure must surface the prompt again so the user can
        // switch to Skip or Abort on a persistently locked item.
        Assert.Null(ManagedStorageMigrationDialog.ResolveStickyItemAction(
            applyToAll: true,
            FileService.FileTransferItemAction.Retry));
    }

    [Fact]
    public void StickyItemAction_AbortNeverSticks()
    {
        Assert.Null(ManagedStorageMigrationDialog.ResolveStickyItemAction(
            applyToAll: true,
            FileService.FileTransferItemAction.Abort));
    }

    [Fact]
    public void StickyItemAction_WithoutApplyAll_NothingSticks()
    {
        Assert.Null(ManagedStorageMigrationDialog.ResolveStickyItemAction(
            applyToAll: false,
            FileService.FileTransferItemAction.Skip));
        Assert.Null(ManagedStorageMigrationDialog.ResolveStickyItemAction(
            applyToAll: false,
            FileService.FileTransferItemAction.Retry));
    }
}
