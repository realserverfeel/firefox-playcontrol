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
    // Master toggle hotkey: enables/disables all the global playback hotkeys at
    // once (like PotPlayer). Stays registered while the app runs so you can
    // re-enable it. Same combo format as the extension.
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+P";
    // When true, suppress the local corner overlay while the browser is the
    // foreground window (the in-page OSD already gives feedback there).
    public bool SuppressOverlayWhenBrowserFocused { get; set; } = true;
    // Legacy: hotkeys are now configured in the extension and synced over the
    // WebSocket; this field is ignored for registration and kept only so old
    // config.json files still parse.
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    // Where the shortcut map synced from the extension is cached, so global
    // hotkeys can be registered on startup before the browser connects.
    public static string ShortcutsCachePath =>
        Path.Combine(AppContext.BaseDirectory, "shortcuts.json");

    public static Dictionary<string, List<string>> LoadSyncedShortcuts()
    {
        try
        {
            if (File.Exists(ShortcutsCachePath))
            {
                var json = File.ReadAllText(ShortcutsCachePath);
                var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, Opts);
                if (map != null) return map;
            }
        }
        catch
        {
            // ignore: treat as no cached shortcuts
        }
        return new Dictionary<string, List<string>>();
    }

    public static void SaveSyncedShortcuts(Dictionary<string, List<string>> map)
    {
        try
        {
            File.WriteAllText(ShortcutsCachePath, JsonSerializer.Serialize(map, Opts));
        }
        catch
        {
            // best-effort
        }
    }

    public static AppConfig Default()
    {
        return new AppConfig
        {
            Port = 8423,
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
                    if (string.IsNullOrWhiteSpace(cfg.ToggleHotkey)) cfg.ToggleHotkey = "Ctrl+Alt+P";
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

    public AppConfig Clone()
    {
        return new AppConfig
        {
            Port = Port,
            ShowOverlay = ShowOverlay,
            OverlayFontSize = OverlayFontSize,
            OverlayColor = OverlayColor,
            OverlayCorner = OverlayCorner,
            OverlayDurationMs = OverlayDurationMs,
            ToggleHotkey = ToggleHotkey,
            SuppressOverlayWhenBrowserFocused = SuppressOverlayWhenBrowserFocused,
            Hotkeys = new Dictionary<string, string>(Hotkeys),
        };
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
