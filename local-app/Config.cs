using System.Text.Json;

namespace PlayControlAgent;

// Persisted configuration, stored next to the executable as config.json.
public class AppConfig
{
    public int Port { get; set; } = 8423;
    public bool ShowOverlay { get; set; } = true;
    public int OverlayFontSize { get; set; } = 22;
    public string OverlayColor { get; set; } = "#3b9dff";
    // top-right | top-left | bottom-right | bottom-left
    public string OverlayCorner { get; set; } = "top-right";
    public int OverlayDurationMs { get; set; } = 1500;
    // Hotkey combo (e.g. "Ctrl+Alt+Right") -> action name understood by the
    // extension (togglePlay, seekBack, seekForward, markBookmark, prevBookmark,
    // nextBookmark, playSegment, copyUrl, clearBookmarks).
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Default()
    {
        return new AppConfig
        {
            Port = 8423,
            Hotkeys = new Dictionary<string, string>
            {
                ["Ctrl+Alt+Home"] = "togglePlay",
                ["Ctrl+Alt+Left"] = "seekBack",
                ["Ctrl+Alt+Right"] = "seekForward",
                ["Ctrl+Alt+Up"] = "prevBookmark",
                ["Ctrl+Alt+Down"] = "nextBookmark",
                ["Ctrl+Alt+End"] = "markBookmark",
            },
        };
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Opts);
                if (cfg != null)
                {
                    cfg.Hotkeys ??= new Dictionary<string, string>();
                    if (cfg.Port <= 0 || cfg.Port > 65535) cfg.Port = 8423;
                    return cfg;
                }
            }
        }
        catch
        {
            // fall through to defaults on any parse/IO error
        }
        var d = Default();
        d.Save();
        return d;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, Opts));
        }
        catch
        {
            // best-effort
        }
    }
}
