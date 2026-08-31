using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class FileReorderBlockTests
{
    [Fact]
    public void ResolveBlockInsertionIndex_SingleItemMatchesForwardDecrementRule()
    {
        Assert.Equal(
            2,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [1],
                insertionIndex: 3,
                totalCount: 4));
        Assert.Equal(
            1,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [3],
                insertionIndex: 1,
                totalCount: 4));
    }

    [Fact]
    public void ResolveBlockInsertionIndex_ContiguousBlockAccountsForEachRemovedSlot()
    {
        Assert.Equal(
            2,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [1, 2],
                insertionIndex: 4,
                totalCount: 4));
    }

    [Fact]
    public void ResolveBlockInsertionIndex_NonContiguousBlockCountsOnlyBeforeBoundary()
    {
        Assert.Equal(
            1,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [1, 3],
                insertionIndex: 2,
                totalCount: 4));
    }

    [Fact]
    public void ResolveBlockInsertionIndex_ClampsToValidRange()
    {
        Assert.Equal(
            0,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [2, 3],
                insertionIndex: 0,
                totalCount: 4));
        Assert.Equal(
            2,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [0, 1],
                insertionIndex: 4,
                totalCount: 4));
        Assert.Equal(
            0,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [0, 1, 2, 3],
                insertionIndex: 2,
                totalCount: 4));
    }

    [Fact]
    public void ResolveBlockInsertionIndex_HandlesEmptyAndOversizedInputs()
    {
        Assert.Equal(
            0,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [],
                insertionIndex: 0,
                totalCount: 0));
        Assert.Equal(
            2,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [0, 1],
                insertionIndex: 100,
                totalCount: 4));
        Assert.Equal(
            0,
            ReorderDropIndexCalculator.ResolveBlockInsertionIndex(
                [],
                insertionIndex: 100,
                totalCount: 4));
    }

    [Fact]
    public void SurfaceUsesBlockReorderInsteadOfSingleItemOffset()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs"));

        Assert.Contains(
            "MoveItemsForReorder(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (targetIndex > currentIndex)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "targetIndex--;",
            source,
            StringComparison.Ordinal);
    }
}
