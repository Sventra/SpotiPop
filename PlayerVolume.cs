using System;
using System.Threading.Tasks;

namespace SpotiPop;

/// <summary>Reads and sets the active player's volume: Spotifast's own volume via its CLI, otherwise the Windows mixer.</summary>
public sealed class PlayerVolume(SpotifyWatcher watcher)
{
    static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(4);

    bool _sending;
    int? _pending;
    int? _cached;
    bool _cachedForSpotifast;
    DateTime _cachedAt;

    /// <summary>Spotifast's volume costs a process launch to read, so recent answers (and our own writes) are reused.</summary>
    public async Task<int?> GetAsync()
    {
        var spotifast = watcher.IsSpotifast;
        if (_cached is { } hit && _cachedForSpotifast == spotifast && DateTime.UtcNow - _cachedAt < CacheLifetime) return hit;

        int? volume;
        if (spotifast)
        {
            volume = await SpotifastCli.GetVolumeAsync();
        }
        else
        {
            SpotifyVolume.Refresh();
            volume = SpotifyVolume.Get() is { } mixer ? (int)Math.Round(mixer * 100) : null;
        }
        Remember(volume, spotifast);
        return volume;
    }

    /// <summary>Spotifast's CLI takes ~100 ms per call, so while dragging only the latest value is sent. Call on the UI thread.</summary>
    public async void Send(int percent)
    {
        Remember(percent, watcher.IsSpotifast);
        if (!watcher.IsSpotifast)
        {
            SpotifyVolume.Set(percent / 100f);
            return;
        }

        _pending = percent;
        if (_sending) return;
        _sending = true;
        try
        {
            while (_pending is { } next)
            {
                _pending = null;
                await SpotifastCli.SetVolumeAsync(next);
            }
        }
        finally { _sending = false; }
    }

    void Remember(int? volume, bool spotifast)
    {
        _cached = volume;
        _cachedForSpotifast = spotifast;
        _cachedAt = DateTime.UtcNow;
    }
}
