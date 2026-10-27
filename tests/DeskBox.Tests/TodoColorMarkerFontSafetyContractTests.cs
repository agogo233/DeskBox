using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// Segoe UI Symbol's font file is removed on Win11 25H2 (26300) while its
/// Fonts registry entry survives. The first measure of a FontIcon pinned to
/// that single family name fails inside CFontFamily::GetTextLineBoundsMetrics
/// with E_UNEXPECTED — a stowed exception that fail-fasts the process the
/// moment the todo color submenu opens (feedback #756). These pins keep the
/// font from being reintroduced from either code or markup.
/// </summary>
public sealed class TodoColorMarkerFontSafetyContractTests
{
    [Theory]
    [InlineData("cs")]
    [InlineData("xaml")]
    public void Sources_NeverPinTheRemovedSegoeUiSymbolFont(string extension)
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src", "DeskBox");

        foreach (string file in Directory.EnumerateFiles(
                     sourceRoot,
                     "*." + extension,
                     SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            string source = File.ReadAllText(file);
            // The leading quote matches both the C# literal ("Segoe UI Symbol")
            // and the XAML attribute (FontFamily="Segoe UI Symbol") while
            // staying silent about prose mentions in comments.
            Assert.False(
                source.Contains("\"Segoe UI Symbol", StringComparison.Ordinal),
                $"{file} pins the removed 'Segoe UI Symbol' font family; use the theme icon font chain or font-free geometry instead.");
        }
    }

    [Fact]
    public void TodoMenus_UseFontFreeGeometryForTheColorDot()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "src", "DeskBox", "Controls", "WidgetContents", "TodoWidgetContent.Menus.cs"));

        Assert.Contains("EllipseGeometry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new FontFamily(", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "DeskBox", "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DeskBox repository root was not found.");
    }
}
