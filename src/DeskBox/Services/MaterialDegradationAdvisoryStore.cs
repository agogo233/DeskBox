using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Services;

// Local persistence machinery (the atomic marker file), so this store sits in
// the Core.Persistence namespace beside WidgetLayoutStore — the module
// boundary keeps destructive file operations out of plain Services code.
namespace DeskBox.Core.Persistence;

/// <summary>
/// Marker written when the user dismisses the frosted-glass degradation
/// advisory ("Don't show again"). The marker makes the one-shot hint survive
/// restarts: the in-process <c>WarnOnce</c> set alone cannot, because every
/// launch is a fresh session while the degrading system switch (transparency
/// effects off, battery saver on) persists across launches. Lives in the data
/// directory so a full restore swaps it away with the fresh-profile data.
/// </summary>
internal sealed record MaterialDegradationAdvisoryDismissal(
    DateTimeOffset DismissedAtUtc);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(
    typeof(MaterialDegradationAdvisoryDismissal),
    TypeInfoPropertyName = "MaterialDegradationAdvisoryDismissal")]
internal sealed partial class MaterialDegradationAdvisoryJsonContext
    : JsonSerializerContext
{
}

/// <summary>
/// Owns <c>material-advisory-dismissed.json</c> in the data directory.
/// Advisory state: a lost or unreadable marker only costs the dismissal (the
/// hint reappears on the next launch), so reads never throw.
/// </summary>
internal sealed class MaterialDegradationAdvisoryStore
{
    internal const string MarkerFileName = "material-advisory-dismissed.json";

    private const long MaxMarkerFileBytes = 64 * 1024;

    private readonly string _markerPath;

    public MaterialDegradationAdvisoryStore()
        : this(DeskBoxDataPathService.Current.DataDirectory)
    {
    }

    internal MaterialDegradationAdvisoryStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _markerPath = Path.Combine(dataDirectory, MarkerFileName);
    }

    internal string MarkerPath => _markerPath;

    /// <summary>
    /// Returns the dismissal timestamp when the user previously chose "Don't
    /// show again", or null when the marker is absent, oversized, unreadable
    /// or corrupt. Advisory state never blocks startup, so every failure path
    /// degrades to "not dismissed".
    /// </summary>
    public DateTimeOffset? TryReadDismissedAtUtc()
    {
        try
        {
            if (!File.Exists(_markerPath) ||
                new FileInfo(_markerPath).Length > MaxMarkerFileBytes)
            {
                return null;
            }

            return JsonSerializer.Deserialize(
                File.ReadAllText(_markerPath),
                MaterialDegradationAdvisoryJsonContext.Default
                    .MaterialDegradationAdvisoryDismissal)?.DismissedAtUtc;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Log(
                $"[Material] Advisory dismissal marker could not be read: {ex.Message}");
            return null;
        }
    }

    public Task MarkDismissedAsync(CancellationToken cancellationToken = default)
        => WriteCoreAsync(
            JsonSerializer.Serialize(
                new MaterialDegradationAdvisoryDismissal(DateTimeOffset.UtcNow),
                MaterialDegradationAdvisoryJsonContext.Default
                    .MaterialDegradationAdvisoryDismissal),
            cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete();
        return Task.CompletedTask;
    }

    private async Task WriteCoreAsync(
        string json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string tempPath = $"{_markerPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _markerPath, overwrite: true);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            // Advisory state never blocks startup: a failed dismissal only
            // costs the marker (the hint reappears on the next launch).
            App.Log(
                $"[Material] Advisory dismissal marker could not be written: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }

    private void TryDelete()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            App.Log(
                $"[Material] Advisory dismissal marker delete failed: {ex.Message}");
        }
    }
}
