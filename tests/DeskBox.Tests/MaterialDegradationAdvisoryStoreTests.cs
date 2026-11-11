using DeskBox.Core.Persistence;

namespace DeskBox.Tests;

/// <summary>
/// Durable "Don't show again" marker for the frosted-glass degradation
/// advisory: marker lifecycle and the read-time failure degradation (missing,
/// corrupt and oversized markers all read as "not dismissed").
/// </summary>
public sealed class MaterialDegradationAdvisoryStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dataDirectory;

    public MaterialDegradationAdvisoryStoreTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            Guid.NewGuid().ToString("N"));
        _dataDirectory = Directory.CreateDirectory(
            Path.Combine(_tempRoot, "data")).FullName;
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void TryReadDismissedAtUtc_ReturnsNull_WhenMarkerMissing()
    {
        var store = new MaterialDegradationAdvisoryStore(_dataDirectory);
        Assert.Null(store.TryReadDismissedAtUtc());
    }

    [Fact]
    public async Task MarkDismissedAsync_WritesReadableMarker()
    {
        var store = new MaterialDegradationAdvisoryStore(_dataDirectory);
        await store.MarkDismissedAsync();

        Assert.True(File.Exists(store.MarkerPath));
        Assert.NotNull(store.TryReadDismissedAtUtc());
    }

    [Fact]
    public async Task ClearAsync_RemovesMarker()
    {
        var store = new MaterialDegradationAdvisoryStore(_dataDirectory);
        await store.MarkDismissedAsync();
        Assert.NotNull(store.TryReadDismissedAtUtc());

        await store.ClearAsync();

        Assert.False(File.Exists(store.MarkerPath));
        Assert.Null(store.TryReadDismissedAtUtc());
    }

    [Fact]
    public void TryReadDismissedAtUtc_ReturnsNull_WhenCorruptJson()
    {
        string path = Path.Combine(
            _dataDirectory,
            MaterialDegradationAdvisoryStore.MarkerFileName);
        File.WriteAllText(path, "{ not valid json ");

        var store = new MaterialDegradationAdvisoryStore(_dataDirectory);
        Assert.Null(store.TryReadDismissedAtUtc());
    }

    [Fact]
    public void TryReadDismissedAtUtc_ReturnsNull_WhenOversized()
    {
        string path = Path.Combine(
            _dataDirectory,
            MaterialDegradationAdvisoryStore.MarkerFileName);
        File.WriteAllText(path, new string(' ', (64 * 1024) + 1));

        var store = new MaterialDegradationAdvisoryStore(_dataDirectory);
        Assert.Null(store.TryReadDismissedAtUtc());
    }
}
