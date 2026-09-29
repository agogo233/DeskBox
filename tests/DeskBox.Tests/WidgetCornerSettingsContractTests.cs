namespace DeskBox.Tests;

public sealed class WidgetCornerSettingsContractTests
{
    [Fact]
    public void CornerSelector_OffersRoundSmallAndSquareWithRoundFirst()
    {
        string editor = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Features/Appearance/AppearanceSettingsViewModel.cs"));

        Assert.Contains(
            "WidgetCornerKinds.Round,\n        WidgetCornerKinds.Small,\n        WidgetCornerKinds.Square",
            editor.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("CornerDefault", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Corner.Default", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void CornerLocalization_NoLongerContainsSystemDefaultOption()
    {
        string stringsRoot = TestPaths.FromRepository("src/DeskBox/Strings");
        foreach (string file in Directory.EnumerateFiles(stringsRoot, "*.json"))
        {
            string json = File.ReadAllText(file);
            Assert.DoesNotContain("\"Settings.Corner.Default\"", json, StringComparison.Ordinal);
        }
    }
}
