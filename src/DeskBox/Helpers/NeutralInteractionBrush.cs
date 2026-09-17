using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskBox.Helpers;

/// <summary>
/// The single neutral palette for transient interaction states: marquee
/// selection, drag insertion indicators, drop previews and hover/selection
/// washes. These states describe what the pointer is doing, not what the
/// content means, so they draw from the theme's monochrome fill and stroke
/// brushes instead of the accent color.
///
/// WinUI's themed tokens cannot be resolved by element theme from code, so
/// the palette is mirrored into the application theme dictionaries under the
/// <see cref="FillSecondaryKey"/> family of keys. Resolution picks the
/// dictionary by the scope element's <see cref="FrameworkElement.ActualTheme"/>,
/// or by the app's effective theme when the caller owns no tree yet; a bare
/// application-scope lookup would follow the system theme and invert the
/// colors whenever the app's theme override disagrees with it.
///
/// The resolved color is a snapshot: it does not follow a later theme flip,
/// so callers that paint with it re-apply from their ActualThemeChanged
/// handlers (the widget surfaces and windows already do).
/// </summary>
public static class NeutralInteractionBrush
{
    public const string FillSecondaryKey = "DeskBoxNeutralFillSecondaryBrush";
    public const string FillTertiaryKey = "DeskBoxNeutralFillTertiaryBrush";
    public const string LineKey = "DeskBoxNeutralLineBrush";
    public const string TextPrimaryKey = "DeskBoxNeutralTextPrimaryBrush";

    /// <summary>Subtle wash for a surface the pointer is over.</summary>
    public static Color Fill(DependencyObject? scope) =>
        ResolveThemedBrush(FillSecondaryKey, scope)?.Color ?? Colors.Transparent;

    /// <summary>Stronger neutral tone for a 1-2px line, bar or marquee outline.</summary>
    public static Color Line(DependencyObject? scope) =>
        ResolveThemedBrush(LineKey, scope)?.Color ?? Colors.Transparent;

    /// <summary>
    /// Resolves one of the mirrored <c>DeskBoxNeutral*</c> brushes for the
    /// scope element's effective theme. The returned instance may be shared
    /// (a host override or the theme dictionary's own brush), so callers must
    /// copy the color instead of mutating it.
    /// </summary>
    public static SolidColorBrush? ResolveThemedBrush(string key, DependencyObject? scope)
    {
        if (scope is FrameworkElement element)
        {
            // A host that overrides the key in its own scope keeps that
            // override.
            for (DependencyObject? current = element;
                 current is not null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is FrameworkElement candidate &&
                    candidate.Resources.TryGetValue(key, out object? scoped) &&
                    scoped is SolidColorBrush scopedBrush)
                {
                    return scopedBrush;
                }
            }
        }

        string themeKey = ResolveThemeDictionaryKey(scope);
        return Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out object? dictionary) &&
            dictionary is ResourceDictionary themed &&
            themed.TryGetValue(key, out object? value) &&
            value is SolidColorBrush brush
            ? brush
            : null;
    }

    /// <summary>
    /// Picks the application theme dictionary to read: the scope element's
    /// resolved theme, or the theme ThemeService would apply to a window root
    /// when the caller owns no tree yet.
    /// </summary>
    private static string ResolveThemeDictionaryKey(DependencyObject? scope)
    {
        ElementTheme theme = scope is FrameworkElement element
            ? element.ActualTheme
            : App.Current.ThemeService?.EffectiveTheme ?? ElementTheme.Light;
        return theme == ElementTheme.Dark ? "Dark" : "Light";
    }
}
