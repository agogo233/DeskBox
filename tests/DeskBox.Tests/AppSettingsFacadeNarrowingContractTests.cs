using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Batch 51 schema-equivalent narrowing contract for the AppSettings facade.
/// The 220 passthrough properties are the frozen settings.json wire contract:
/// every passthrough is an on-disk schema member, so the facade may neither
/// grow (new schema field without a versioned migration) nor shrink (dropping
/// a serialized field from every saved file) as part of ordinary refactoring.
/// Serializing slice members directly was evaluated and rejected: the flat
/// member order interleaves all 13 slices and the legacy
/// <c>widgetCapsuleModeEnabled</c> wire attributes live on the facade, so no
/// byte-equivalent slice-serialization path exists (the disk schema is
/// frozen; <see cref="SettingsSliceContractBaselineTests"/> pins the exact
/// member order and pre-slice defaults).
/// </summary>
public sealed class AppSettingsFacadeNarrowingContractTests
{
    private static readonly PropertyInfo[] Passthroughs = typeof(AppSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p is { CanRead: true, CanWrite: true }
                    && p.Name != nameof(AppSettings.SchemaVersion)
                    && !p.PropertyType.Name.EndsWith("SettingsSlice", StringComparison.Ordinal))
        .ToArray();

    private static readonly PropertyInfo[] SliceAccessors = typeof(AppSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.PropertyType.Name.EndsWith("SettingsSlice", StringComparison.Ordinal))
        .ToArray();

    private static readonly HashSet<string> SerializedMemberNames = SerializeMemberNames();

    [Fact]
    public void PassthroughCount_IsFrozenAtBatch51Level()
    {
        // 220 = the complete batch-51 schema surface (13 slice accessors and
        // SchemaVersion are the only other members). Growing this count adds a
        // settings.json field without a schema-versioned migration; shrinking
        // it drops a field from every file written henceforth. Either change
        // is a disk-schema decision — update this pin consciously alongside
        // the SettingsSliceContractBaselineTests order pin.
        Assert.Equal(220, Passthroughs.Length);
        Assert.Equal(13, SliceAccessors.Length);
    }

    [Fact]
    public void EveryPassthrough_IsAFrozenSchemaMember_AndViceVersa()
    {
        // The facade IS the schema: with the WhenWritingNull member forced on,
        // the serialized member set is exactly schemaVersion plus the 220
        // passthrough wire names (camelCase unless a JsonPropertyName
        // overrides it). This proves there is no dead passthrough sitting
        // outside the wire, and no wire member without a passthrough owner.
        var expected = Passthroughs
            .Select(WireName)
            .ToHashSet(StringComparer.Ordinal);
        expected.Add("schemaVersion");
        Assert.True(
            SerializedMemberNames.SetEquals(expected),
            "Serialized member set must equal schemaVersion + the 220 passthrough wire names.");
        Assert.Equal(221, SerializedMemberNames.Count);
    }

    [Fact]
    public void SliceAccessors_AreNeverSerialized()
    {
        // Ownership slices stay memory-only: nesting them into the wire would
        // change the flat pre-slice shape, so their accessors must remain
        // [JsonIgnore] get-only and absent from the serialized member set.
        Assert.All(SliceAccessors, p =>
        {
            Assert.False(p.CanWrite, $"{p.Name} must be get-only");
            Assert.NotNull(p.GetCustomAttribute<JsonIgnoreAttribute>());
        });
        Assert.All(SerializedMemberNames, name =>
            Assert.DoesNotContain(name, SliceAccessors.Select(p => WireName(p))));
    }

    [Fact]
    public void WireLevelAttributes_AreFrozenOnTheFacade()
    {
        // The single legacy wire-attribute pair (rename + null omission for
        // the migration-era widgetCapsuleModeEnabled key) is a root cause of
        // why slice-direct serialization cannot be byte-equivalent. It must
        // stay exactly here, exactly this shape, exactly this count.
        PropertyInfo[] renamed = Passthroughs
            .Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
            .ToArray();
        PropertyInfo[] conditionallyIgnored = Passthroughs
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
                            == JsonIgnoreCondition.WhenWritingNull)
            .ToArray();

        Assert.Single(renamed);
        Assert.Single(conditionallyIgnored);
        Assert.Same(renamed[0], conditionallyIgnored[0]);
        Assert.Equal(nameof(AppSettings.LegacyWidgetCapsuleModeEnabled), renamed[0].Name);
        Assert.Equal(
            "widgetCapsuleModeEnabled",
            renamed[0].GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);
    }

    private static HashSet<string> SerializeMemberNames()
    {
        // Legacy prop set so the WhenWritingNull member is emitted too.
        var settings = new AppSettings { LegacyWidgetCapsuleModeEnabled = true };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            settings, SettingsJsonContext.Default.AppSettings));
        return doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string WireName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
}
