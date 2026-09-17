namespace DeskBox.ViewModels;

/// <summary>
/// Batch mutation scope for bulk item imports. A 2000-file import used to
/// pay the per-item derived work 2000 times: a full NormalizeSortOrder pass
/// over the list, a manual-order persistence check, an AddedAt settings
/// persist, a hydration restart (generation bump plus cancellation of the
/// still-running pass), and — wherever an await let the dispatcher run a
/// queued callback — a render window reconcile and a full stack-display
/// rebuild. Inside the scope those reactions are deferred; disposing the
/// scope runs each of them exactly once against the settled item list.
/// </summary>
public partial class WidgetViewModel
{
    private int _itemMutationBatchDepth;
    private bool _itemMutationBatchDirty;
    private bool _addedAtPersistPending;
    private bool _pendingFolderRefreshAfterBatch;

    /// <summary>
    /// True while a bulk mutation is in flight. Watcher events that land
    /// inside the window fold into the open batch — their derived work is
    /// deferred and covered by the same finalization — instead of paying
    /// their own per-item costs mid-import. Full watcher reloads are the
    /// exception: they bypass every per-item gate (and a large import
    /// reliably trips the watcher's reload threshold), so they are deferred
    /// to one authoritative refresh after the batch.
    /// </summary>
    internal bool IsItemMutationBatchActive => _itemMutationBatchDepth > 0;

    /// <summary>
    /// Opens a batch mutation scope. Disposing it finalizes: one sort-order
    /// normalization pass, one manual-order persistence check, one AddedAt
    /// persistence, one stack-display rebuild, one render-window reconcile
    /// (which in turn re-checks viewport coverage and then starts hydration
    /// against the settled prefix), and at most one deferred watcher
    /// refresh. An exception inside the scope still finalizes — callers
    /// hold it across try/finally or using blocks.
    /// </summary>
    internal IDisposable EnterItemMutationScope()
    {
        _itemMutationBatchDepth++;
        return new ItemMutationScope(this);
    }

    private void MarkItemMutationBatchDirty() => _itemMutationBatchDirty = true;

    private sealed class ItemMutationScope : IDisposable
    {
        private WidgetViewModel? _owner;

        public ItemMutationScope(WidgetViewModel owner) => _owner = owner;

        public void Dispose()
        {
            WidgetViewModel? owner = _owner;
            _owner = null;
            if (owner is null || owner._itemMutationBatchDepth <= 0)
            {
                return;
            }

            owner._itemMutationBatchDepth--;
            if (owner._itemMutationBatchDepth > 0 || !owner._itemMutationBatchDirty)
            {
                return;
            }

            owner._itemMutationBatchDirty = false;
            owner.NormalizeSortOrder();
            owner.PersistManualOrderSnapshotIfChanged();
            if (owner._addedAtPersistPending)
            {
                owner._addedAtPersistPending = false;
                owner.PersistAddedAtTracking();
            }

            owner.QueueStackDisplayRebuild();
            // Hydration must not start here: the render reconcile below only
            // enqueues, and every hydration pipeline snapshots the rendered
            // prefix synchronously at startup. QueuePostBatchHydration hands
            // the start to that callback, so hydration reads the settled
            // prefix, not the stale one.
            owner.QueuePostBatchHydration();
            if (owner._pendingFolderRefreshAfterBatch)
            {
                owner._pendingFolderRefreshAfterBatch = false;
                _ = owner.RunDeferredFolderRefreshAsync();
            }
        }
    }
}
