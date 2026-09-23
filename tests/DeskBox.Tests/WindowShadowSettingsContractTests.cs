namespace DeskBox.Tests;

/// <summary>
/// Pins the appearance-section window-shadow toggle contract: it mirrors the
/// Windows-wide "show shadows under windows" effect through SystemParametersInfo
/// and must always warn the user about the global scope before writing.
/// </summary>
public sealed class WindowShadowSettingsContractTests
{
    [Fact]
    public void AppearanceSection_ExposesSystemShadowToggleWithExplicitScopeLabeling()
    {
        string xaml = Read("src/DeskBox/Views/SettingsSections/AppearanceSettingsSection.xaml");

        Assert.Contains("x:Name=\"WindowShadowToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Toggled=\"WindowShadowToggle_Toggled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.WindowShadow.Title", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.WindowShadow.Description", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToggleHandler_ConfirmsGlobalScopeBeforeWritingAndReadsBackResult()
    {
        string code = Read("src/DeskBox/Views/SettingsSections/AppearanceSettingsSection.xaml.cs");
        string handler = Slice(
            code,
            "private async void WindowShadowToggle_Toggled",
            "private async Task<bool> ConfirmWindowShadowChangeAsync");

        Assert.Contains("ConfirmWindowShadowChangeAsync()", handler, StringComparison.Ordinal);
        Assert.Contains("TrySetWindowDropShadowEnabled(target", handler, StringComparison.Ordinal);
        Assert.Contains("TryGetWindowDropShadowEnabled(out bool", handler, StringComparison.Ordinal);
        Assert.Contains("applied.Value != target", handler, StringComparison.Ordinal);
        Assert.Contains("RefreshWindowShadowToggle()", handler, StringComparison.Ordinal);

        // SPIF_SENDCHANGE broadcasts to every top-level window and waits on
        // each — the write must never run on the settings UI thread.
        Assert.Contains("await Task.Run(", handler, StringComparison.Ordinal);
        Assert.Contains("WindowShadowToggle.IsEnabled = false", handler, StringComparison.Ordinal);

        string confirm = Slice(
            code,
            "private async Task<bool> ConfirmWindowShadowChangeAsync",
            "private async Task ShowWindowShadowFailureAsync");

        Assert.Contains("Settings.WindowShadow.Confirm.Title", confirm, StringComparison.Ordinal);
        Assert.Contains("Settings.WindowShadow.Confirm.Body", confirm, StringComparison.Ordinal);
        Assert.Contains("ContentDialogResult.Primary", confirm, StringComparison.Ordinal);
    }

    [Fact]
    public void Win32Helper_WritesDropShadowThroughSystemParametersInfoWithBroadcast()
    {
        string source = Read("src/DeskBox/Platform/Win32Helper.cs");

        Assert.Contains("SpiGetDropShadow = 0x1024", source, StringComparison.Ordinal);
        Assert.Contains("SpiSetDropShadow = 0x1025", source, StringComparison.Ordinal);
        Assert.Contains("SpifUpdateIniFile", source, StringComparison.Ordinal);
        Assert.Contains("SpifSendChange", source, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfo", source, StringComparison.Ordinal);
        Assert.Contains("TryGetWindowDropShadowEnabled", source, StringComparison.Ordinal);
        Assert.Contains("TrySetWindowDropShadowEnabled", source, StringComparison.Ordinal);

        // pvParam is polymorphic: SPI_SETDROPSHADOW wants the BOOL passed by
        // value — a marshalled pointer is always nonzero and would be read as
        // TRUE, silently turning "disable" into "enable".
        string setter = Slice(
            source,
            "public static bool TrySetWindowDropShadowEnabled",
            "[StructLayout");
        Assert.Contains("(IntPtr)(enabled ? 1 : 0)", setter, StringComparison.Ordinal);
        Assert.DoesNotContain("ref ", setter, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowShadowStrings_PresentInEveryShippedLocale()
    {
        string[] requiredKeys =
        [
            "Settings.WindowShadow.Title",
            "Settings.WindowShadow.Description",
            "Settings.WindowShadow.Confirm.Title",
            "Settings.WindowShadow.Confirm.Body",
            "Settings.WindowShadow.ApplyFailed",
            "Settings.WindowShadow.ApplyFailedNoEffect"
        ];
        string stringsDirectory = TestPaths.FromRepository("src/DeskBox/Strings");

        foreach (string path in Directory.EnumerateFiles(stringsDirectory, "*.json"))
        {
            string content = File.ReadAllText(path);
            foreach (string key in requiredKeys)
            {
                Assert.Contains("\"" + key + "\"", content, StringComparison.Ordinal);
            }
        }
    }

    private static string Read(string path)
    {
        return File.ReadAllText(TestPaths.FromRepository(path));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }
}
