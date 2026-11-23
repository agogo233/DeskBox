using DeskBox.Services;
using System.Text;

namespace DeskBox.Tests;

/// <summary>
/// Temporary diagnostics for the CI-only empty locale table mystery: the
/// upstream-identical tests (WeatherCodeMapper, ...) resolve every T() to its
/// key on CI while the same tables load fine from the repository. This test
/// always fails and prints the probe results into the CI log.
/// </summary>
public sealed class LocalizationLoadingDiagnosticsTests
{
    [Fact]
    public void Diagnose_LocaleStream_Resolution()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
        sb.AppendLine($"CurrentDirectory: {Directory.GetCurrentDirectory()}");
        sb.AppendLine($"LocalizationAssembly: {typeof(LocalizationService).Assembly.Location}");
        sb.AppendLine($"TestAssembly: {typeof(LocalizationLoadingDiagnosticsTests).Assembly.Location}");
        sb.AppendLine($"LocalizationAssembly MVID: {typeof(LocalizationService).Assembly.ManifestModule.ModuleVersionId}");

        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 16 && !string.IsNullOrEmpty(dir); depth++)
        {
            string stringsDir = Path.Combine(dir, "Strings");
            string? dirFiles = null;
            if (Directory.Exists(stringsDir))
            {
                dirFiles = string.Join(",", Directory.EnumerateFiles(stringsDir).Select(Path.GetFileName).Order().Take(6));
            }

            sb.AppendLine(
                $"depth {depth}: {dir} | " +
                $"StringsDir={Directory.Exists(stringsDir)} [{dirFiles ?? "-"}] | " +
                $"repoFile={File.Exists(Path.Combine(dir, "src", "DeskBox", "Strings", "zh-CN.json"))}");
            dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dir));
        }

        var loc = TestServices.CreateLocalizationService();
        sb.AppendLine($"T(Weather.Condition.0) = {loc.T("Weather.Condition.0")}");
        sb.AppendLine($"T(Settings.WindowTitle) = {loc.T("Settings.WindowTitle")}");

        Assert.Fail(sb.ToString());
    }
}
