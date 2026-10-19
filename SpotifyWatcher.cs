using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SpotiPop;

public sealed record TrackInfo(string Title, string Artist, BitmapImage? Art, Color Accent);

/// <summary>Follows Spotify through the Windows media transport controls (the same API behind the volume flyout).</summary>
public sealed class SpotifyWatcher
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    string? _key;
    bool _hasArt;
    int _generation;

    public TrackInfo? Current { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsSpotifast => _session?.SourceAppUserModelId.Contains("spotifast", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Second argument is true when a new song started (popup should appear), false for silent refreshes.</summary>
    public event Action<TrackInfo, bool>? TrackChanged;
    public event Action<bool>? PlayingChanged;
    public event Action? PlayerGone;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => Attach();
        _manager.CurrentSessionChanged += (_, _) => Attach();
        Attach(announce: false);
    }

    public void Previous() => _session?.TrySkipPreviousAsync();
    public void Next() => _session?.TrySkipNextAsync();
    public void TogglePlay() => _session?.TryTogglePlayPauseAsync();

    void Attach(bool announce = true)
    {
        var next = PickSession();
        if (next?.SourceAppUserModelId == _session?.SourceAppUserModelId) return;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
        }
        _session = next;
        if (next is null)
        {
            // Forget the song so the same one is announced again when a player comes back.
            _key = null;
            _hasArt = false;
            Current = null;
            IsPlaying = false;
            _ui.Post(_ => PlayerGone?.Invoke(), null);
            return;
        }

        next.MediaPropertiesChanged += OnMediaChanged;
        next.PlaybackInfoChanged += OnPlaybackChanged;
        OnPlaybackChanged(next, null);
        _ = RefreshAsync(announce);
    }

    /// <summary>Spotify (Store/desktop) or Spotifast; with both open, follow the one the system considers current, then whichever is playing.</summary>
    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        var candidates = _manager!.GetSessions().Where(s => IsSupportedPlayer(s.SourceAppUserModelId)).ToList();
        var current = _manager.GetCurrentSession();
        return candidates.FirstOrDefault(s => s.SourceAppUserModelId == current?.SourceAppUserModelId)
            ?? candidates.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? candidates.FirstOrDefault();
    }

    static bool IsSupportedPlayer(string appUserModelId) =>
        appUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase)
        || appUserModelId.Contains("spotifast", StringComparison.OrdinalIgnoreCase);

    void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, object? _) => _ = RefreshAsync(announce: true);

    void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, object? _)
    {
        var playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playing == IsPlaying) return;
        IsPlaying = playing;
        _ui.Post(_ => PlayingChanged?.Invoke(playing), null);
    }

    async Task RefreshAsync(bool announce)
    {
        // Spotify fires several events per song change and fills in the cover a moment later: debounce them.
        var generation = Interlocked.Increment(ref _generation);
        await Task.Delay(250);
        if (generation != _generation || _session is not { } session) return;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (string.IsNullOrWhiteSpace(props.Title)) return;

            var art = await LoadArtAsync(props.Thumbnail);
            var key = $"{props.Artist}\u0001{props.Title}";
            var isNewSong = key != _key;
            if (!isNewSong && (art is null || _hasArt)) return;

            _key = key;
            _hasArt = art is not null;
            var info = new TrackInfo(props.Title, props.Artist, art, AccentColor.From(art));
            Current = info;
            var show = isNewSong && announce;
            _ui.Post(_ => TrackChanged?.Invoke(info, show), null);
        }
        catch { /* session vanished mid-query; the next event retries */ }
    }

    static async Task<BitmapImage?> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256; // covers are 640 px; the biggest we draw is ~100 px
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
