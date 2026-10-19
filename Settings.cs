using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotiPop;

public enum IslandEdge { Top, Bottom }
public enum ControlsPlacement { Below, Side }
public enum ControlsVisibility { Near, Always }

public sealed class Settings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotiPop", "settings.json");

    /// <summary>Card that slides up from the bottom-right when the song changes.</summary>
    public bool SongPopup { get; set; } = true;

    /// <summary>Always-on thin player at the top or bottom of a screen.</summary>
    public bool Island { get; set; }
    public IslandEdge Edge { get; set; } = IslandEdge.Top;
    /// <summary>Screen device name; null means the primary screen.</summary>
    public string? Monitor { get; set; }
    public ControlsPlacement Controls { get; set; } = ControlsPlacement.Below;
    public ControlsVisibility ControlsShown { get; set; } = ControlsVisibility.Near;

    public event Action? Changed;

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>Persists and tells listeners (island, popup) to re-read the values.</summary>
    public void Apply()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings still apply for this session */ }
        Changed?.Invoke();
    }
}
