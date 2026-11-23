namespace DeskBox.Models;

// Calendar contract models consumed by ICalendarPresentationSource. They live
// in the Abstractions assembly so account providers and CalDAV clients can
// later feed the contract without depending on the host application. The
// view-side GlanceCalendarDayDecoration bridge stays with the Glance widget
// because its Native AOT generated bindable provider is bound from widget XAML.

/// <summary>Statutory-holiday marker for one day (China mainland schedule).</summary>
public enum GlanceHolidayMark
{
    None,
    Off,
    Work
}

public sealed record GlanceCalendarDay(
    DateOnly Date,
    string DayText,
    bool IsCurrentMonth,
    bool IsToday,
    string TraditionalText = "",
    string FestivalText = "",
    GlanceHolidayMark HolidayMark = GlanceHolidayMark.None,
    string HolidayName = "",
    int AgendaCount = 0)
{
    public bool HasTraditionalText => !string.IsNullOrWhiteSpace(TraditionalText);
    public bool HasFestival => !string.IsNullOrWhiteSpace(FestivalText);
    public bool HasTraditionalTextOnly => HasTraditionalText && !HasFestival;
    public bool HasSecondaryText => HasFestival || HasTraditionalText;
}

/// <summary>One due-dated item surfaced from a To-do widget.</summary>
public sealed record GlanceAgendaItem(
    string Id,
    string Title,
    DateOnly Date,
    TimeOnly? Time,
    bool IsImportant,
    string? ColorHex,
    string SourceWidgetId);

public sealed record GlanceCalendarMonth(
    DateOnly Month,
    IReadOnlyList<string> WeekdayHeaders,
    IReadOnlyList<GlanceCalendarDay> Days,
    string TraditionalTitle = "")
{
    public bool HasTraditionalTitle => !string.IsNullOrWhiteSpace(TraditionalTitle);
}

public sealed record GlanceCalendarEvent(
    string Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsAllDay,
    string? CalendarColor = null);
