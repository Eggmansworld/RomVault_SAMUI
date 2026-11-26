using System.Text.Json;
using SAM_UI_Avalonia.Models;
using TrrntZip;

namespace SAM_UI_Avalonia.Core;

/// <summary>
/// App settings, saved as SAM-UI.settings.json next to the exe (or in %LocalAppData%\SAM-UI
/// when that folder is read-only).
///
/// Input/output formats are deliberately NOT remembered from the last run. The app always
/// starts on the user's saved defaults, so a one-off format change can't silently carry
/// over into the next (possibly hours long) job.
/// </summary>
public sealed class SamConfig
{
    /// <summary>Bumped when a new release changes a default that should replace old saved values.</summary>
    public int ConfigVersion { get; set; }
    private const int CurrentVersion = 2;

    // --- processing (the user's saved defaults; the app always starts on these)
    public InputZipType DefaultInput { get; set; } = InputZipType.Zip;
    public OutputType DefaultOutput { get; set; } = OutputType.ZipTorrent;
    /// <summary>Files processed at once. 2 is a safe start; the user can save another default.</summary>
    public int DefaultThreads { get; set; } = 2;

    // --- window (size in device-independent pixels; always opens centred on the primary screen)
    public double WindowWidth { get; set; } = 1680;
    public double WindowHeight { get; set; } = 1050;
    public bool WindowMaximized { get; set; }

    // --- look
    public string Theme { get; set; } = Fx.Skin.All[0].Name;
    public bool Effects { get; set; } = true;
    public bool Scanlines { get; set; } = true;
    public string Shape { get; set; } = "AUTO";
    public bool ShowScroller { get; set; } = true;
    public bool ShowStarfield { get; set; } = true;
    public bool ShowObjects { get; set; } = true;
    public bool ShowCopper { get; set; } = true;
    public bool ShowBalls { get; set; } = true;
    public bool ShowFloor { get; set; } = true;
    public int BallCount { get; set; } = 24;
    public int StarCount { get; set; } = 180;

    // --- jukebox
    /// <summary>Start the default tune when the app opens.</summary>
    public bool AutoPlayMusic { get; set; } = true;
    /// <summary>User's own start-up song (full path). Empty = play whatever was playing at last exit.</summary>
    public string? StartupTune { get; set; }
    public bool JukeboxEnabled { get; set; }
    public string? JukeboxTune { get; set; }
    public int JukeboxSub { get; set; }
    public int JukeboxVolume { get; set; } = 50;
    public int SidSeconds { get; set; } = 180;
    public bool JukeboxShuffle { get; set; }
    /// <summary>Per SID file name (lower case): subtunes to play. Missing = all music subtunes.</summary>
    public Dictionary<string, List<int>> SidPicks { get; set; } = new()
    {
        // Only the title music of Yie Ar Kung Fu; the other subtunes are game jingles.
        ["yie ar kung fu (martin galway).sid"] = new List<int> { 19 },
    };

    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder { get; } = PickFolder();
    public static string FilePath => Path.Combine(Folder, "SAM-UI.settings.json");

    public static SamConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                SamConfig cfg = JsonSerializer.Deserialize<SamConfig>(File.ReadAllText(FilePath), Json) ?? new SamConfig();
                if (cfg.ConfigVersion < 2)
                {
                    cfg.JukeboxVolume = 50; // v1.2 default: half volume
                    cfg.DefaultThreads = 2;
                }
                cfg.ConfigVersion = CurrentVersion;
                return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Settings file unreadable, using defaults: " + ex.Message);
        }
        return new SamConfig { ConfigVersion = CurrentVersion };
    }

    public void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save settings: " + ex.Message);
        }
    }

    private static string PickFolder()
    {
        string dir = AppContext.BaseDirectory;
        try
        {
            string probe = Path.Combine(dir, ".samui_write_test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }
        catch
        {
            string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM-UI");
            Directory.CreateDirectory(alt);
            return alt;
        }
    }
}

/// <summary>Tiny diagnostics log (Debug output only; the app has no log window).</summary>
public static class Log
{
    public static void Info(string t) => System.Diagnostics.Debug.WriteLine("[INFO] " + t);
    public static void Warn(string t) => System.Diagnostics.Debug.WriteLine("[WARN] " + t);
    public static void Error(string t) => System.Diagnostics.Debug.WriteLine("[ERROR] " + t);
}
