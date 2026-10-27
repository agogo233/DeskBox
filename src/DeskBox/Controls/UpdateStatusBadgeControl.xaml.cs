using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls;

/// <summary>
/// The 20px status badge for the settings update card (a close port of
/// PowerToys' UpdateStateBadgeControl): gradient green check when current,
/// accent clock while checking, accent download glyph while downloading,
/// amber attention when an update waits, red cross after a failed download.
/// State arrives as a string (see <see cref="UpdateStatusBadgeStates"/>)
/// because the AOT bindable-property surface binds strings.
/// </summary>
public sealed partial class UpdateStatusBadgeControl : UserControl
{
    public static readonly DependencyProperty StateNameProperty = DependencyProperty.Register(
        nameof(StateName),
        typeof(string),
        typeof(UpdateStatusBadgeControl),
        new PropertyMetadata(UpdateStatusBadgeStates.Success, OnStateNameChanged));

    public string StateName
    {
        get => (string)GetValue(StateNameProperty);
        set => SetValue(StateNameProperty, value);
    }

    public UpdateStatusBadgeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisualState();
    }

    private static void OnStateNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UpdateStatusBadgeControl)d).UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        string stateName = StateName switch
        {
            UpdateStatusBadgeStates.Checking => "CheckingState",
            UpdateStatusBadgeStates.Downloading => "DownloadingState",
            UpdateStatusBadgeStates.Attention => "AttentionState",
            UpdateStatusBadgeStates.Error => "ErrorState",
            _ => "SuccessState",
        };
        VisualStateManager.GoToState(this, stateName, useTransitions: false);
    }
}
