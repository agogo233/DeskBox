using System.Diagnostics;
using DeskBox.Core.Persistence;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Frosted-glass degradation advisory: the "Don't show again" dismissal is
/// checked before any platform probe, so the skip behaviour is deterministic
/// without a Windows session. The material-type gate is asserted directly too.
/// </summary>
public sealed class MaterialCapabilityAdvisorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly MaterialDegradationAdvisoryStore _store;

    public MaterialCapabilityAdvisorTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            Guid.NewGuid().ToString("N"));
        string dataDirectory = Directory.CreateDirectory(
            Path.Combine(_tempRoot, "data")).FullName;
        _store = new MaterialDegradationAdvisoryStore(dataDirectory);
        MaterialCapabilityAdvisor.SetStoreForTesting(_store);
    }

    public void Dispose()
    {
        MaterialCapabilityAdvisor.SetStoreForTesting(null);
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void IsDismissed_False_WhenNoMarker()
    {
        Assert.False(MaterialCapabilityAdvisor.IsDismissed());
    }

    [Fact]
    public async Task MarkDismissed_SetsCachedDismissed_AndWritesMarker()
    {
        MaterialCapabilityAdvisor.MarkDismissed();

        Assert.True(MaterialCapabilityAdvisor.IsDismissed());
        await WaitUntilAsync(() => File.Exists(_store.MarkerPath));
        Assert.NotNull(_store.TryReadDismissedAtUtc());
    }

    [Fact]
    public async Task WarnIfMaterialDegraded_SkipsWhenAlreadyDismissed()
    {
        // Simulate a prior session that chose "Don't show again": the marker
        // exists on disk, the in-process cache is cold.
        await _store.MarkDismissedAsync();
        MaterialCapabilityAdvisor.SetStoreForTesting(_store);

        int warned = 0;
        MaterialCapabilityAdvisor.WarnIfMaterialDegraded(
            "Mica",
            (_, _) => warned++);

        Assert.Equal(0, warned);
    }

    [Fact]
    public void WarnIfMaterialDegraded_SkipsForSolidMaterial()
    {
        // Solid has no glass to degrade; the material gate fires before any
        // platform probe, so this is deterministic on any host.
        int warned = 0;
        MaterialCapabilityAdvisor.WarnIfMaterialDegraded(
            "Solid",
            (_, _) => warned++);

        Assert.Equal(0, warned);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException(
                    "Condition was not satisfied before the timeout.");
            }

            await Task.Delay(20);
        }
    }
}
