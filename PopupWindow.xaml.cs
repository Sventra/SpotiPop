using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SpotiPop;

public partial class PopupWindow : Window
{
    const double VolumeStep = 5;
    const double ShadowPad = 64; // Root margin: room for the bloom
    const double ScreenGap = 16;

    readonly SpotifyWatcher _watcher;
    readonly Settings _settings;
    readonly PlayerVolume _volume;
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool _hiding;
    bool _suppressVolume;

    public PopupWindow(SpotifyWatcher watcher, Settings settings, PlayerVolume volume)
    {
        InitializeComponent();
        _watcher = watcher;
        _settings = settings;
        _volume = volume;

        _hideTimer.Tick += (_, _) =>
        {
            if (!Fullscreen.IsActive() && IsMouseOver) return;
            _hideTimer.Stop();
            BeginHide();
        };
        Root.MouseLeave += (_, _) => { _hideTimer.Stop(); _hideTimer.Start(); };

        watcher.TrackChanged += (track, announce) =>
        {
            Apply(track);
            if (announce && _settings.SongPopup) Reveal();
        };
        watcher.PlayingChanged += SetPlaying;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTricks.MakeOverlay(this);
    }

    /// <summary>Shows the popup for the current song (also used by the tray icon).</summary>
    public void Reveal()
    {
        if (_watcher.Current is null || Fullscreen.IsActive()) return;
        Apply(_watcher.Current);
        SetPlaying(_watcher.IsPlaying);
        SyncVolume();

        if (!IsVisible || _hiding)
        {
            _hiding = false;
            if (!IsVisible)
            {
                Root.Opacity = 0;
                Show();
            }
            UpdateLayout();
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth + ShadowPad - ScreenGap;
            Top = area.Bottom - ActualHeight + ShadowPad - ScreenGap;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            Slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    void BeginHide()
    {
        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (!_hiding) return;
            _hiding = false;
            Hide();
            MemoryTrim.Request();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, TimeSpan.FromMilliseconds(180)));
    }

    void Apply(TrackInfo track)
    {
        TitleText.Text = track.Title;
        ArtistText.Text = track.Artist;
        SetAccent(track.Accent);
        ArtBorder.Background = track.Art is null
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new ImageBrush(track.Art) { Stretch = Stretch.UniformToFill };
        ArtPlaceholder.Visibility = track.Art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void SetAccent(Color color)
    {
        Resources["AccentColor"] = color;
        Resources["AccentDimColor"] = AccentColor.Dim(color);
        foreach (var glow in new[] { CardGlow, ArtGlow, PlayGlow })
            glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(500)));
    }

    void SetPlaying(bool playing) => PlayButton.Content = playing ? "" : "";

    async void SyncVolume()
    {
        if (await _volume.GetAsync() is not { } value) return;
        _suppressVolume = true;
        VolumeSlider.Value = value;
        _suppressVolume = false;
    }

    void KeepOpen()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    void Previous_Click(object sender, RoutedEventArgs e) { _watcher.Previous(); KeepOpen(); }
    void Next_Click(object sender, RoutedEventArgs e) { _watcher.Next(); KeepOpen(); }
    void Play_Click(object sender, RoutedEventArgs e) { _watcher.TogglePlay(); KeepOpen(); }

    void VolumeDown_Click(object sender, RoutedEventArgs e) => VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - VolumeStep);
    void VolumeUp_Click(object sender, RoutedEventArgs e) => VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + VolumeStep);

    void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(e.Delta) * VolumeStep, 0, 100);
        e.Handled = true;
    }

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var percent = (int)Math.Round(e.NewValue);
        VolumeText.Text = $"{percent}%";
        if (_suppressVolume || percent == (int)Math.Round(e.OldValue)) return;
        _volume.Send(percent);
        KeepOpen();
    }
}
