using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SpotiPop;

/// <summary>
/// Spotifast keeps its own (librespot) volume, separate from the Windows mixer, and exposes it only through its CLI.
/// </summary>
public static class SpotifastCli
{
    const int VolumeColumn = 6; // `now-playing --raw`: state, title, artists, album, position_ms, duration_ms, volume, ...

    static string? _exePath;

    public static async Task<int?> GetVolumeAsync()
    {
        var output = await RunAsync("now-playing", "--raw");
        var fields = output?.Split('\t');
        return fields is { Length: > VolumeColumn } && int.TryParse(fields[VolumeColumn], out var volume) ? volume : null;
    }

    public static Task SetVolumeAsync(int percent) => RunAsync("volume", Math.Clamp(percent, 0, 100).ToString());

    static string? ExePath()
    {
        if (_exePath is not null && File.Exists(_exePath)) return _exePath;

        // Prefer the running instance's path, fall back to the default per-user install location.
        _exePath = Process.GetProcessesByName("spotifast")
            .Select(p => { try { return p.MainModule?.FileName; } catch { return null; } })
            .FirstOrDefault(path => path is not null)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Fastpotify", "spotifast.exe");
        return File.Exists(_exePath) ? _exePath : null;
    }

    static async Task<string?> RunAsync(params string[] args)
    {
        if (ExePath() is not { } exe) return null;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null) return null;
            var read = process.StandardOutput.ReadToEndAsync();
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token);
            return await read;
        }
        catch { return null; }
    }
}
