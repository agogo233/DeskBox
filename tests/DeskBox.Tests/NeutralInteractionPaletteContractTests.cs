namespace DeskBox.Tests;

/// <summary>
/// Pins the palette rule the interaction states were rebuilt around: hover,
/// press, selection, marquee, drag insertion and drop targets are neutral,
/// while the accent is reserved for semantic emphasis (progress, badges,
/// material theming, the user's accent picker, primary buttons).
///
/// The audit reads the sources because the rule is about which accent sources
/// a state is allowed to reach for; a state that reintroduces one of the
/// retired accent helpers fails here instead of only showing up on screen.
/// </summary>
public sealed class NeutralInteractionPaletteContractTests
{
    [Fact]
    public void MarqueeSelection_UsesTheNeutralPalette()
    {
        string visuals = Read("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ItemVisuals.cs");
        string todo = Read("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.EditingAndUndo.cs");
        string quickCapture = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.Appearance.cs");
        string search = Read("src/DeskBox/Views/SearchPopupWindow.xaml");

        Assert.Contains("NeutralInteractionBrush.Fill(", visuals, StringComparison.Ordinal);
        Assert.Contains("NeutralInteractionBrush.Line(", visuals, StringComparison.Ordinal);
        Assert.Contains("NeutralInteractionBrush.Fill(", todo, StringComparison.Ordinal);
        Assert.Contains("NeutralInteractionBrush.Fill(", quickCapture, StringComparison.Ordinal);
        Assert.Contains(
            "SubtleFillColorSecondaryBrush",
            ElementDeclaration(search, "RubberBandRect"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AccentFillColorDefaultBrush",
            ElementDeclaration(search, "RubberBandRect"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DragInsertionIndicators_DoNotReachForTheAccent()
    {
        string visuals = Read("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ItemVisuals.cs");
        string surface = Read("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs");
        string todo = Read("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.DragDrop.cs");
        string quickCapture = Read("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");
        string quickCaptureWindow = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.ItemActions.cs");
        string stackPopover = Read("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.StackPopover.cs");

        string reorderDrop = MethodBody(todo, "void ApplyTodoReorderDropState(");
        Assert.Contains("NeutralInteractionBrush.Line(", reorderDrop, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", reorderDrop, StringComparison.Ordinal);

        string quickCaptureReorder = MethodBody(quickCapture, "void ApplyQuickCaptureReorderDropState(");
        Assert.Contains("NeutralInteractionBrush.Line(", quickCaptureReorder, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", quickCaptureReorder, StringComparison.Ordinal);

        string quickCaptureItem = MethodBody(quickCapture, "void ApplyQuickCaptureItemDropState(");
        Assert.Contains("NeutralInteractionBrush.Line(", quickCaptureItem, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", quickCaptureItem, StringComparison.Ordinal);
        // The previous fallback was a hardcoded accent blue, not a neutral.
        Assert.DoesNotContain("0x78, 0x9E, 0xFF", quickCaptureItem, StringComparison.Ordinal);

        string quickCaptureWindowReorder = MethodBody(quickCaptureWindow, "void SetItemReorderDropState(");
        Assert.Contains("NeutralInteractionBrush.Line(", quickCaptureWindowReorder, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", quickCaptureWindowReorder, StringComparison.Ordinal);

        // The file surface keeps the accent for import progress only.
        string accentVisuals = MethodBody(surface, "private void ApplyAccentVisuals()");
        Assert.Contains("NeutralInteractionBrush.Line(", accentVisuals, StringComparison.Ordinal);
        Assert.Contains("ImportProgressBar.Foreground", accentVisuals, StringComparison.Ordinal);
        Assert.Contains("ReorderInsertionAccentStop.Color = NeutralInteractionBrush.Line(", accentVisuals, StringComparison.Ordinal);
        Assert.Contains("ReorderInsertionLine.Background", accentVisuals, StringComparison.Ordinal);

        // The stack popover indicator is built and repainted without the accent.
        Assert.DoesNotContain(
            "_stackPopoverReorderIndicator.Background =\r\n                SharedBrushCache.GetOrCreate(materialAppearance.AccentColor)",
            stackPopover,
            StringComparison.Ordinal);
        Assert.Contains("NeutralInteractionBrush.Line(_stackPopoverReorderIndicator)", stackPopover, StringComparison.Ordinal);
    }

    [Fact]
    public void HoverAndSelectionStates_DrawFromTheNeutralPalette()
    {
        string todo = Read("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.EditingAndUndo.cs");
        string quickCaptureWindow = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.ItemActions.cs");
        string titleSwitcher = Read("src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml.cs");
        string styleCache = Read("src/DeskBox/Controls/FileItemSurfaceStyleCache.cs");
        string groupDrop = Read("src/DeskBox/Controls/WidgetShell.xaml.cs");

        string todoHover = MethodBody(todo, "void SetTodoItemHoverState(");
        Assert.Contains("NeutralInteractionBrush.Fill(", todoHover, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAccentSurfaceColor", todoHover, StringComparison.Ordinal);

        string quickCaptureHover = MethodBody(quickCaptureWindow, "void SetItemHoverState(");
        Assert.Contains("NeutralInteractionBrush.Fill(", quickCaptureHover, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildAccentSurfaceColor", quickCaptureHover, StringComparison.Ordinal);

        string wheelFeedback = MethodBody(titleSwitcher, "void ApplyWheelFeedbackAccent(");
        Assert.Contains("NeutralInteractionBrush.Line(", wheelFeedback, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleIconAccentColor", wheelFeedback, StringComparison.Ordinal);

        string groupDropAppearance = MethodBody(groupDrop, "void ApplyGroupDropPreviewAppearance(");
        Assert.Contains("NeutralInteractionBrush.Line(", groupDropAppearance, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", groupDropAppearance, StringComparison.Ordinal);

        // The shared tile cache no longer takes an accent at all: every drop
        // target is the same neutral hover surface with no border.
        Assert.DoesNotContain("accentColor", styleCache, StringComparison.Ordinal);
        Assert.DoesNotContain("_dropTargetSurfaceBrush", styleCache, StringComparison.Ordinal);
        Assert.DoesNotContain("_dropTargetBorderBrush", styleCache, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentationPointerStates_AreNeutralWhileSelectionKeepsTheAccent()
    {
        string helper = Read("src/DeskBox/Services/WidgetSegmentedStyleHelper.cs");
        string weather = Read("src/DeskBox/Controls/WidgetContents/WeatherWidgetContent.xaml.cs");

        Assert.Contains(
            "ApplyNeutralPointerStates",
            helper,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"PivotItemBackgroundPointerOver\"",
            helper,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"PivotItemBackgroundPressed\"",
            helper,
            StringComparison.Ordinal);
        // The selected segment is a deliberate choice, so the accent stays.
        Assert.DoesNotContain(
            "\"PivotItemBackgroundSelected\"",
            helper,
            StringComparison.Ordinal);
        // Weather reuses the shared entry point instead of its own copy.
        Assert.Contains("ApplyNeutralPointerStates", weather, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentResourceScope.Apply", weather, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeSnapGuides_KeepTheAccentColor()
    {
        // Simon's explicit call (2026-09-13): resizing/drag-snap feedback reads
        // better in the theme color, so the guide tone is the accent even
        // though the other drag indicators are neutral.
        string guides = Read("src/DeskBox/Services/ResizeGuideOverlayService.cs");
        string body = MethodBody(guides, "Windows.UI.Color GetHighlightColor()");

        Assert.Contains("GetEffectiveAccentColor", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NeutralInteractionBrush", body, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupPositionRails_UseTheNeutralPaletteAndRefreshOnThemeFlip()
    {
        string titleSwitcher = Read("src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml.cs");
        string shell = Read("src/DeskBox/Controls/WidgetShell.xaml.cs");

        string rail = MethodBody(titleSwitcher, "void SetPositionRail(");
        Assert.Contains("NeutralInteractionBrush.Line(", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleIconAccentColor", rail, StringComparison.Ordinal);

        string compactRail = MethodBody(shell, "void UpdateCompactGroupPositionRail(");
        Assert.Contains("NeutralInteractionBrush.Line(", compactRail, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleIconAccentColor", compactRail, StringComparison.Ordinal);

        // Both rails copy colors at paint time, so a theme flip must re-render.
        Assert.Contains(
            "SetPositionRail(CurrentPositionRailLayer, _displayedIdentity)",
            titleSwitcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "UpdateCompactGroupPositionRail(_groupPresentation)",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InputSurfaces_FollowTheInlineRenamePrecedent()
    {
        // The search field, the search popup's focus ring and the query-match
        // highlight are input chrome: neutral like the inline rename box, with
        // no accent wash when the input is active.
        string quickCaptureAppearance = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.Appearance.cs");
        string searchPopup = Read("src/DeskBox/Views/SearchPopupWindow.xaml.cs");
        string quickCaptureItems = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.Items.cs");

        string searchVisual = MethodBody(quickCaptureAppearance, "void ApplySearchVisualStyle(");
        Assert.Contains("NeutralInteractionBrush.Line(", searchVisual, StringComparison.Ordinal);
        Assert.DoesNotContain("accentColor", searchVisual, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSearchExpanded", searchVisual, StringComparison.Ordinal);

        string gotFocus = MethodBody(searchPopup, "void SearchTextBox_GotFocus(");
        Assert.Contains("NeutralInteractionBrush.Line(", gotFocus, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEffectiveAccentColor", gotFocus, StringComparison.Ordinal);

        string highlight = MethodBody(quickCaptureItems, "void ApplyItemSearchHighlight(");
        Assert.DoesNotContain("GetEffectiveAccentColor", highlight, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeFlips_ReapplyCopiedNeutralChrome()
    {
        // These visuals copy theme-dependent colors into brushes at apply time,
        // so a light/dark flip must run their appliers again.
        string quickCaptureWindow = Read("src/DeskBox/Views/QuickCaptureWidgetWindow.xaml.cs");
        string quickCaptureSurface = Read("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        Assert.Contains(
            "ApplySegmentedStyle();",
            MethodBody(quickCaptureWindow, "void OnRootElementThemeChanged("),
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplySegmentedStyle();",
            MethodBody(quickCaptureSurface, "void QuickCaptureSurfaceContent_ActualThemeChanged("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NeutralInteractionResolution_FollowsTheElementTheme()
    {
        // Code-created neutral visuals must resolve by the scope element's
        // theme (or the app's effective theme), never by a bare
        // application-scope lookup: that follows the system theme and inverts
        // the colors whenever the app's theme override disagrees with it.
        string helper = Read("src/DeskBox/Helpers/NeutralInteractionBrush.cs");
        string segmented = Read("src/DeskBox/Services/WidgetSegmentedStyleHelper.cs");
        string app = Read("src/DeskBox/App.xaml");
        string themeService = Read("src/DeskBox/Services/ThemeService.cs");

        Assert.Contains("ActualTheme", helper, StringComparison.Ordinal);
        Assert.Contains("EffectiveTheme", helper, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Application.Current.Resources.TryGetValue(",
            helper,
            StringComparison.Ordinal);
        Assert.Contains(
            "NeutralInteractionBrush.ResolveThemedBrush",
            segmented,
            StringComparison.Ordinal);
        Assert.Contains(
            "public ElementTheme EffectiveTheme",
            themeService,
            StringComparison.Ordinal);
        foreach (string key in new[]
        {
            "DeskBoxNeutralFillSecondaryBrush",
            "DeskBoxNeutralFillTertiaryBrush",
            "DeskBoxNeutralLineBrush",
            "DeskBoxNeutralTextPrimaryBrush",
        })
        {
            Assert.Contains(key, app, StringComparison.Ordinal);
        }
    }

    private static string MethodBody(string source, string marker)
    {
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {marker}");
        int end = source.Length;
        foreach (string nextMember in new[] { "\n    private", "\n    internal", "\n    public", "\n    protected" })
        {
            int candidate = source.IndexOf(
                nextMember,
                start + marker.Length,
                StringComparison.Ordinal);
            if (candidate >= 0 && candidate < end)
            {
                end = candidate;
            }
        }

        return source[start..end];
    }

    /// <summary>
    /// The element named <paramref name="elementName"/> plus the lines that
    /// follow it, which is where its properties are declared.
    /// </summary>
    private static string ElementDeclaration(string source, string elementName)
    {
        int start = source.IndexOf(elementName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing element: {elementName}");
        int end = source.IndexOf("/>", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "src",
                    "DeskBox",
                    "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "DeskBox repository root was not found.");
    }
}
