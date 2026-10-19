using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SpotiPop;

/// <summary>Controls the per-app volume of Spotify / Spotifast in the Windows mixer.</summary>
public static class SpotifyVolume
{
    static readonly string[] ProcessNames = ["Spotify", "spotifast"];

    static MMDevice? _device;
    static List<AudioSessionControl>? _sessions;

    /// <summary>Forget cached sessions so the next call picks up players that started since.</summary>
    public static void Refresh() => Reset();

    public static float? Get() => With(sessions =>
        (sessions.FirstOrDefault(s => s.State == AudioSessionState.AudioSessionStateActive) ?? sessions[0]).SimpleAudioVolume.Volume);

    public static void Set(float volume) => With(sessions =>
    {
        foreach (var s in sessions)
        {
            s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
            s.SimpleAudioVolume.Mute = false;
        }
        return 0f;
    });

    static float? With(Func<List<AudioSessionControl>, float> action)
    {
        // Cached sessions go stale when a player restarts or the output device changes: retry once with a fresh lookup.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _sessions ??= FindSessions();
                return _sessions.Count == 0 ? null : action(_sessions);
            }
            catch { Reset(); }
        }
        return null;
    }

    static void Reset()
    {
        _sessions = null;
        _device?.Dispose();
        _device = null;
    }

    static List<AudioSessionControl> FindSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var all = _device.AudioSessionManager.Sessions;
        var found = new List<AudioSessionControl>();
        for (var i = 0; i < all.Count; i++)
        {
            if (IsPlayer(all[i])) found.Add(all[i]);
        }
        return found;
    }

    static bool IsPlayer(AudioSessionControl s)
    {
        try
        {
            var pid = (int)s.GetProcessID;
            if (pid == 0) return false;
            var name = Process.GetProcessById(pid).ProcessName;
            return ProcessNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
}
