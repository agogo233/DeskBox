using DeskBox.Helpers;

namespace DeskBox.Tests;

public sealed class WeatherCodeMapperLocalizationTests
{
    [Theory]
    [InlineData("zh-TW", "晴")]
    [InlineData("hi-IN", "साफ आसमान")]
    [InlineData("es-ES", "Cielo despejado")]
    [InlineData("fr-FR", "Ciel dégagé")]
    [InlineData("ar-SA", "سماء صافية")]
    [InlineData("bn-BD", "পরিষ্কার আকাশ")]
    [InlineData("ru-RU", "Ясное небо")]
    public void NewLocales_LocalizeWeatherDescription(string locale, string expected)
    {
        Assert.Equal(expected, WeatherCodeMapper.GetDescription(0, locale));
    }

    [Theory]
    [InlineData(0, true, "clear-day")]
    [InlineData(0, false, "clear-night")]
    [InlineData(1, true, "clear-day")]
    [InlineData(2, true, "partly-cloudy-day")]
    [InlineData(2, false, "partly-cloudy-night")]
    [InlineData(3, true, "overcast")]
    [InlineData(45, true, "fog")]
    [InlineData(48, false, "fog")]
    [InlineData(51, true, "drizzle")]
    [InlineData(57, true, "drizzle")]
    [InlineData(61, true, "rain")]
    [InlineData(66, true, "sleet")]
    [InlineData(67, true, "sleet")]
    [InlineData(71, true, "snow")]
    [InlineData(77, true, "snow")]
    [InlineData(80, true, "rain-showers")]
    [InlineData(81, true, "rain")]
    [InlineData(85, true, "snow")]
    [InlineData(95, true, "thunderstorms")]
    [InlineData(96, true, "thunderstorms-hail")]
    [InlineData(99, true, "thunderstorms-hail")]
    [InlineData(-1, true, "clear-day")]
    [InlineData(-1, false, "clear-night")]
    public void GetIconUri_MapsWmoCodeToBundledSvg(
        int code, bool isDay, string fileName)
    {
        Assert.Equal(
            $"ms-appx:///Assets/WeatherIcons/{fileName}.svg",
            WeatherCodeMapper.GetIconUri(code, isDay, "DeskBox").ToString());
    }

    [Theory]
    [InlineData(0, true, "flat/clear-day")]
    [InlineData(0, false, "flat/clear-night")]
    [InlineData(2, true, "flat/partly-cloudy-day")]
    [InlineData(45, false, "flat/fog-night")]
    [InlineData(80, true, "flat/partly-cloudy-day-rain")]
    [InlineData(80, false, "flat/partly-cloudy-night-rain")]
    [InlineData(86, true, "flat/partly-cloudy-day-snow")]
    [InlineData(96, true, "flat/thunderstorms-hail")]
    [InlineData(3, true, "line/overcast")]
    [InlineData(-1, false, "flat/clear-night")]
    public void GetIconUri_MeteoconsStyles_UseMeteoconsNames(
        int code, bool isDay, string relativePath)
    {
        string style = relativePath.Split('/')[0];
        Assert.Equal(
            $"ms-appx:///Assets/WeatherIcons/{relativePath}.svg",
            WeatherCodeMapper.GetIconUri(code, isDay, style).ToString());
    }

    [Theory]
    [InlineData(0, true, "fluent/clear-day")]
    [InlineData(0, false, "fluent/clear-night")]
    [InlineData(2, true, "fluent/partly-cloudy-day")]
    [InlineData(2, false, "fluent/partly-cloudy-night")]
    [InlineData(3, true, "fluent/overcast")]
    [InlineData(45, true, "fluent/fog")]
    [InlineData(51, true, "fluent/drizzle")]
    [InlineData(61, true, "fluent/rain")]
    [InlineData(66, true, "fluent/sleet")]
    [InlineData(71, true, "fluent/snow")]
    [InlineData(80, true, "fluent/rain-showers")]
    [InlineData(95, true, "fluent/thunderstorms")]
    [InlineData(96, true, "fluent/thunderstorms-hail")]
    [InlineData(-1, false, "fluent/clear-night")]
    public void GetIconUri_FluentStyle_UsesFluentSubdirWithDeskBoxNames(
        int code, bool isDay, string relativePath)
    {
        Assert.Equal(
            $"ms-appx:///Assets/WeatherIcons/{relativePath}.svg",
            WeatherCodeMapper.GetIconUri(code, isDay, "Fluent").ToString());
    }

    [Fact]
    public void GetIconUri_UnknownOrEmojiStyle_FallsBackToDeskBoxSet()
    {
        // The retired "Emoji" persisted value is now just an unknown style.
        Assert.Equal(
            "ms-appx:///Assets/WeatherIcons/clear-day.svg",
            WeatherCodeMapper.GetIconUri(0, style: "Emoji").ToString());
        Assert.Equal(
            "ms-appx:///Assets/WeatherIcons/clear-day.svg",
            WeatherCodeMapper.GetIconUri(0, style: "nonsense").ToString());
    }

    [Fact]
    public void GetIconUri_EveryMappedFile_ExistsOnDisk()
    {
        string iconsDir = Path.Combine(
            TestPaths.FromRepository("src/DeskBox/Assets/WeatherIcons"));
        foreach (string fileName in new[]
        {
            "clear-day", "clear-night", "partly-cloudy-day",
            "partly-cloudy-night", "overcast", "fog", "drizzle", "rain",
            "sleet", "snow", "rain-showers", "thunderstorms",
            "thunderstorms-hail"
        })
        {
            Assert.True(
                File.Exists(Path.Combine(iconsDir, fileName + ".svg")),
                $"Missing weather icon asset: {fileName}.svg");
            Assert.True(
                File.Exists(Path.Combine(iconsDir, "fluent", fileName + ".svg")),
                $"Missing Fluent weather icon asset: fluent/{fileName}.svg");
        }
    }
}
