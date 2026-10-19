using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SpotiPop;

/// <summary>
/// Always-on thin player. The window is a fixed transparent stage; the pill inside grows and shrinks
/// with spring animations (like the iPhone's dynamic island) when the mouse comes near or a song changes.
/// </summary>
public partial class IslandWindow : Window
{
    const double MinCompactWidth = 150;
    const double MaxCompactWidth = 360;
    const double MinBelowWidth = 270; // room for the transport buttons and volume slider
    const double RowChrome = 8 + 32 + 10 + 6 + 8 + 8; // row margins, art, text margins, border and slack
    const double SideControlsWidth = 108;
    const double BelowControlsHeight = 92;
    const double VolumeStep = 5;
    static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1);        // nothing to track but fullscreen apps
    static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(100); // cursor tracking for "mouse is near"
    static readonly TimeSpan FullscreenCheckEvery = TimeSpan.FromSeconds(1);
    static readonly TimeSpan PeekDuration = TimeSpan.FromSeconds(3.5);

    readonly SpotifyWatcher _watcher;
    readonly Settings _settings;
    readonly PlayerVolume _volume;
    readonly DispatcherTimer _poll = new() { Interval = IdleInterval };

    bool _pillShown;
    bool _expanded;
    bool _suppressVolume;
    bool _fullscreen;
    DateTime _nextFullscreenCheck;
    DateTime _peekUntil;
    DateTime _flashUntil;
    DateTime _lastVolumeSync;
    string _artist = "";
    double _compactWidth = MinCompactWidth;

    public IslandWindow(SpotifyWatcher watcher, Settings settings, PlayerVolume volume)
    {
        InitializeComponent();
        _watcher = watcher;
        _settings = settings;
        _volume = volume;

        PillClip.SizeChanged += (_, e) =>
            PillClip.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 25, 25);
        _poll.Tick += (_, _) => Poll();

        watcher.TrackChanged += OnTrackChanged;
        watcher.PlayingChanged += SetPlaying;
        watcher.PlayerGone += UpdateVisibility;
        settings.Changed += ApplySettings;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTricks.MakeOverlay(this);
    }

    /// <summary>The island exists only while enabled and a player is running.</summary>
    bool Wanted => _settings.Island && _watcher.Current is not null;

    /// <summary>Re-reads settings: shows, hides, repositions and resets the controls layout.</summary>
    public void ApplySettings()
    {
        var top = _settings.Edge == IslandEdge.Top;
        Pill.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Pill.RenderTransformOrigin = new Point(0.5, top ? 0 : 1);
        if (Wanted) Apply(_watcher.Current!);
        ResetControls();
        _fullscreen = Wanted && Fullscreen.IsActive();
        _nextFullscreenCheck = DateTime.UtcNow + FullscreenCheckEvery;
        UpdateVisibility();
        if (_pillShown) Poll();
    }

    /// <summary>Shows or hides the pill, and runs the timer only while there is something to watch.</summary>
    void UpdateVisibility()
    {
        if (Wanted && !_fullscreen)
        {
            SetPlaying(_watcher.IsPlaying);
            ShowPill();
            Place();
            SyncVolume();
        }
        else
        {
            HidePill();
        }

        _poll.Interval = _pillShown && _settings.ControlsShown == ControlsVisibility.Near ? HoverInterval : IdleInterval;
        if (Wanted) _poll.Start();
        else _poll.Stop();
    }

    void OnTrackChanged(TrackInfo track, bool newSong)
    {
        if (!_settings.Island) return;
        Apply(track);

        if (!_pillShown)
        {
            ApplySettings();
            return;
        }
        if (!newSong) return;

        SyncVolume();
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(TargetWidth(_expanded), TimeSpan.FromMilliseconds(520)) { EasingFunction = Spring(6) });
        _peekUntil = DateTime.UtcNow + PeekDuration;
        TextShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        TextHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = Spring(6) };
        ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    void Apply(TrackInfo track)
    {
        _artist = track.Artist;
        _compactWidth = MeasureCompactWidth(track.Title, track.Artist);
        TitleText.Text = track.Title;
        ArtistText.Text = track.Artist;
        _flashUntil = default;
        Art.Background = track.Art is null
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new ImageBrush(track.Art) { Stretch = Stretch.UniformToFill };

        Resources["AccentColor"] = track.Accent;
        Resources["AccentDimColor"] = AccentColor.Dim(track.Accent);
        IslandGlow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(track.Accent, TimeSpan.FromMilliseconds(500)));
    }

    void SetPlaying(bool playing)
    {
        var glyph = playing ? "" : "";
        SidePlay.Content = glyph;
        BelowPlay.Content = glyph;
    }

    // ---- Show / hide ----------------------------------------------------------------------------

    void ShowPill()
    {
        if (_pillShown) return;
        _pillShown = true;
        if (!IsVisible) Show();
        Place();

        Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = Spring(5) });
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(650)) { EasingFunction = Spring(5) });
        _peekUntil = DateTime.UtcNow + PeekDuration;
    }

    void HidePill()
    {
        if (!_pillShown) return;
        _pillShown = false;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) =>
        {
            if (_pillShown) return;
            Hide();
            MemoryTrim.Request();
        };
        Pill.BeginAnimation(OpacityProperty, fade);
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    void Place()
    {
        var screen = WindowTricks.FindScreen(_settings.Monitor);
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * dpi.DpiScaleX);
        var height = (int)Math.Round(Height * dpi.DpiScaleY);
        var bounds = screen.Bounds;
        var work = screen.WorkingArea;
        var x = bounds.X + (bounds.Width - width) / 2;
        var y = _settings.Edge == IslandEdge.Top ? work.Top : work.Bottom - height;
        WindowTricks.MoveTo(this, x, y);
    }

    // ---- Expand / collapse ----------------------------------------------------------------------

    static EasingFunctionBase Spring(double springiness) =>
        new ElasticEase { Oscillations = 1, Springiness = springiness, EasingMode = EasingMode.EaseOut };

    /// <summary>Snaps the controls shut without animating (used when settings change).</summary>
    void ResetControls()
    {
        _expanded = false;
        Pill.BeginAnimation(WidthProperty, null);
        Pill.Width = _compactWidth;
        foreach (var host in new[] { SideHost, BelowHost })
        {
            host.BeginAnimation(OpacityProperty, null);
            host.Opacity = 0;
        }
        SideHost.BeginAnimation(WidthProperty, null);
        SideHost.Width = 0;
        BelowHost.BeginAnimation(HeightProperty, null);
        BelowHost.Height = 0;
    }

    double TargetWidth(bool expanded) => !expanded ? _compactWidth
        : _settings.Controls == ControlsPlacement.Side ? _compactWidth + SideControlsWidth
        : Math.Max(_compactWidth, MinBelowWidth);

    /// <summary>Width of the thin row that fits the song name and artist, so short titles leave no dead space.</summary>
    double MeasureCompactWidth(string title, string artist)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double Measure(TextBlock block, string text) => new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize, Brushes.White, dpi).WidthIncludingTrailingWhitespace;

        // "Volume 100%" replaces the artist line while scrolling, so never go narrower than that.
        var text = Math.Max(Measure(TitleText, title), Math.Max(Measure(ArtistText, artist), Measure(ArtistText, "Volume 100%")));
        return Math.Clamp(Math.Ceiling(text + RowChrome), MinCompactWidth, MaxCompactWidth);
    }

    void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;

        var side = _settings.Controls == ControlsPlacement.Side;
        var pillWidth = TargetWidth(expanded);
        var host = side ? SideHost : BelowHost;
        var property = side ? WidthProperty : HeightProperty;
        var hostSize = !expanded ? 0 : side ? SideControlsWidth : BelowControlsHeight;

        // Springy overshoot on the way out; a plain ease on the way back so nothing bounces while closing.
        IEasingFunction ease = expanded ? Spring(6) : new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(expanded ? 620 : 320);

        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(pillWidth, duration) { EasingFunction = ease });
        host.BeginAnimation(property, new DoubleAnimation(hostSize, duration) { EasingFunction = ease });
        host.BeginAnimation(OpacityProperty, new DoubleAnimation(expanded ? 1 : 0, TimeSpan.FromMilliseconds(expanded ? 260 : 140))
        {
            BeginTime = TimeSpan.FromMilliseconds(expanded ? 110 : 0),
        });

        if (expanded) SyncVolume();
        MemoryTrim.Request();
    }

    void Poll()
    {
        var now = DateTime.UtcNow;
        if (now >= _nextFullscreenCheck)
        {
            _nextFullscreenCheck = now + FullscreenCheckEvery;
            var fullscreen = Fullscreen.IsActive();
            if (fullscreen != _fullscreen)
            {
                _fullscreen = fullscreen;
                UpdateVisibility();
            }
        }
        if (!_pillShown) return;

        if (_flashUntil != default && now > _flashUntil)
        {
            _flashUntil = default;
            ArtistText.Text = _artist;
        }

        var want = _settings.ControlsShown == ControlsVisibility.Always
            || now < _peekUntil
            || IsMouseCaptureWithin
            || NearCursor(_expanded ? 28 : 44);
        SetExpanded(want);
    }

    bool NearCursor(double marginDips)
    {
        if (!WindowTricks.TryGetCursor(out var cx, out var cy)) return false;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var topLeft = Pill.PointToScreen(new Point(0, 0));
        var bottomRight = Pill.PointToScreen(new Point(Pill.ActualWidth, Pill.ActualHeight));
        var margin = marginDips * scale;
        return cx >= topLeft.X - margin && cx <= bottomRight.X + margin
            && cy >= topLeft.Y - margin && cy <= bottomRight.Y + margin;
    }

    // ---- Controls -------------------------------------------------------------------------------

    void Previous_Click(object sender, RoutedEventArgs e) => _watcher.Previous();
    void Next_Click(object sender, RoutedEventArgs e) => _watcher.Next();
    void Play_Click(object sender, RoutedEventArgs e) => _watcher.TogglePlay();

    async void SyncVolume()
    {
        _lastVolumeSync = DateTime.UtcNow;
        if (await _volume.GetAsync() is not { } value) return;
        _suppressVolume = true;
        VolumeSlider.Value = value;
        _suppressVolume = false;
    }

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var percent = (int)Math.Round(e.NewValue);
        VolumeText.Text = $"{percent}%";
        if (_suppressVolume || percent == (int)Math.Round(e.OldValue)) return;
        _volume.Send(percent);
    }

    /// <summary>Scrolling over the island changes the volume even while it is collapsed.</summary>
    async void Pill_Wheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var delta = Math.Sign(e.Delta) * VolumeStep;
        if (DateTime.UtcNow - _lastVolumeSync > TimeSpan.FromSeconds(3))
        {
            _lastVolumeSync = DateTime.UtcNow;
            if (await _volume.GetAsync() is { } fresh)
            {
                _suppressVolume = true;
                VolumeSlider.Value = fresh;
                _suppressVolume = false;
            }
        }

        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
        ArtistText.Text = $"Volume {(int)Math.Round(VolumeSlider.Value)}%";
        _flashUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
    }
}
