#if DESKBOX_NATIVE_AOT
namespace DeskBox.Features.Weather;

// The Weather settings section keeps its runtime {Binding} surface and binds
// through a section-level DataContext switch. Expose only the properties
// used by that XAML surface in NativeAOT builds, mirroring the
// Glance/Music/FileStack/QuickCapture/Todo editor bridge pattern.
[WinRT.GeneratedBindableCustomProperty([
    nameof(AvailableDataSourceOptions),
    nameof(AvailableDefaultViewOptions),
    nameof(AvailableLocationModeOptions),
    nameof(AvailableRefreshIntervalOptions),
    nameof(AvailableSkinOptions),
    nameof(AvailableTemperatureUnitOptions),
    nameof(AvailableWindSpeedUnitOptions),
    nameof(CitySearchPlaceholder),
    nameof(CitySearchText),
    nameof(CitySuggestionItems),
    nameof(DisplayOptionsSummaryText),
    nameof(LocationStatusText),
    nameof(NoCityResultsText),
    nameof(SelectedDataSource),
    nameof(SelectedDefaultView),
    nameof(SelectedLocationMode),
    nameof(SelectedRefreshInterval),
    nameof(SelectedSkin),
    nameof(SelectedTemperatureUnit),
    nameof(SelectedWindSpeedUnit),
    nameof(ShowCitySearch),
    nameof(ShowLocationStatus),
    nameof(ShowNoCityResults)
], [])]
public sealed partial class WeatherSettingsViewModel
{
}
#endif
