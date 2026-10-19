using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace SpotiPop;

public partial class SettingsWindow : Window
{
    readonly Settings _settings;

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        SourceInitialized += (_, _) => WindowTricks.UseDarkTitleBar(this);

        PopupBox.IsChecked = settings.SongPopup;
        IslandBox.IsChecked = settings.Island;
        EdgeTop.IsChecked = settings.Edge == IslandEdge.Top;
        EdgeBottom.IsChecked = settings.Edge == IslandEdge.Bottom;
        ControlsBelow.IsChecked = settings.Controls == ControlsPlacement.Below;
        ControlsSide.IsChecked = settings.Controls == ControlsPlacement.Side;
        ShownNear.IsChecked = settings.ControlsShown == ControlsVisibility.Near;
        ShownAlways.IsChecked = settings.ControlsShown == ControlsVisibility.Always;
        BuildMonitorChips();
        IslandOptions.IsEnabled = settings.Island;
        IslandOptions.Opacity = settings.Island ? 1 : 0.4;
    }

    void BuildMonitorChips()
    {
        var selected = WindowTricks.FindScreen(_settings.Monitor);
        var screens = Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var chip = new RadioButton
            {
                GroupName = "Monitor",
                Style = (Style)FindResource("ChipRadio"),
                Tag = screen.DeviceName,
                Content = $"{i + 1} · {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " (main)" : "")}",
                IsChecked = screen.DeviceName == selected.DeviceName,
            };
            chip.Click += Changed;
            MonitorPanel.Children.Add(chip);
        }
    }

    void Changed(object sender, RoutedEventArgs e)
    {
        _settings.SongPopup = PopupBox.IsChecked == true;
        _settings.Island = IslandBox.IsChecked == true;
        _settings.Edge = EdgeBottom.IsChecked == true ? IslandEdge.Bottom : IslandEdge.Top;
        _settings.Controls = ControlsSide.IsChecked == true ? ControlsPlacement.Side : ControlsPlacement.Below;
        _settings.ControlsShown = ShownAlways.IsChecked == true ? ControlsVisibility.Always : ControlsVisibility.Near;
        _settings.Monitor = MonitorPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

        IslandOptions.IsEnabled = _settings.Island;
        IslandOptions.Opacity = _settings.Island ? 1 : 0.4;
        _settings.Apply();
    }
}
