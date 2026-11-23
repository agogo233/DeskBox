using DeskBox.Services;
using System.Reflection;
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
        sb.AppendLine($"LocalizationAssembly: {typeof(LocalizationService).Assembly.Location}");
        sb.AppendLine($"LocalizationAssembly MVID: {typeof(LocalizationService).Assembly.ManifestModule.ModuleVersionId}");

        string stringsDir = Path.Combine(AppContext.BaseDirectory, "Strings");
        if (Directory.Exists(stringsDir))
        {
            foreach (string file in Directory.EnumerateFiles(stringsDir).Order())
            {
                var fi = new FileInfo(file);
                string head = File.Exists(file)
                    ? File.ReadAllText(file, Encoding.UTF8)[..Math.Min(120, (int)Math.Min(fi.Length, 120))].Replace("\r", "\\r").Replace("\n", "\\n")
                    : "<missing>";
                sb.AppendLine($"{Path.GetFileName(file)}: {fi.Length} bytes | {head}");
            }
        }
        else
        {
            sb.AppendLine("bin Strings dir: MISSING");
        }

        foreach (string field in new[] { "_zhCn", "_enUs", "_arSa" })
        {
            var fi = typeof(LocalizationService).GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            var table = fi?.GetValue(null) as System.Collections.IDictionary;
            sb.AppendLine($"{field}: {(table is null ? "null" : $"{table.Count} entries")}");
        }

        sb.AppendLine($"TranslateForCulture(en-US, Weather.Condition.0) = {LocalizationService.TranslateForCulture("en-US", "Weather.Condition.0")}");
        sb.AppendLine($"TranslateForCulture(zh-CN, Weather.Condition.0) = {LocalizationService.TranslateForCulture("zh-CN", "Weather.Condition.0")}");

        var loc = TestServices.CreateLocalizationService();
        sb.AppendLine($"CurrentCultureName = {loc.CurrentCultureName}");
        sb.AppendLine($"T(Weather.Condition.0) = {loc.T("Weather.Condition.0")}");
        sb.AppendLine($"T(Settings.WindowTitle) = {loc.T("Settings.WindowTitle")}");

        Assert.Fail(sb.ToString());
    }
}
