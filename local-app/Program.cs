using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace PlayControlAgent;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.SystemAware); } catch { /* ignore */ }
        Application.Run(new TrayAppContext());
    }
}

// Owns the tray icon, hotkey registration, WebSocket server and overlay, and
// wires them together. Lives for the lifetime of the process.
public class TrayAppContext : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "PlayControlAgent";

    AppConfig _cfg;
    readonly NotifyIcon _tray;
    readonly HotkeyManager _hotkeys;
    readonly OverlayForm _overlay;
    readonly Icon _appIcon;
    WsServer _server;
    bool _connected;
    bool _enabled = true;

    // Shortcut map (action -> [combos]) synced from the extension. Cached to
    // disk so the same global hotkeys are registered on startup, before the
    // browser connects.
    Dictionary<string, List<string>> _shortcuts;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["togglePlay"] = "Play / Pause",
        ["seekBack"] = "\u23EA Backward",
        ["seekForward"] = "\u23E9 Forward",
        ["markBookmark"] = "\u2691 Bookmark",
        ["prevBookmark"] = "\u23EE Prev bookmark",
        ["nextBookmark"] = "\u23ED Next bookmark",
        ["playSegment"] = "\uD83D\uDD01 Play segment",
        ["copyUrl"] = "Copy URL",
        ["clearBookmarks"] = "Clear bookmarks",
    };

    public TrayAppContext()
    {
        _cfg = AppConfig.Load();
        _shortcuts = AppConfig.LoadSyncedShortcuts();
        _appIcon = LoadAppIcon();

        _overlay = new OverlayForm(_cfg);
        // Force handle creation so the first Flash is instant and so we have a
        // UI-thread control to marshal callbacks onto.
        _ = _overlay.Handle;

        _hotkeys = new HotkeyManager();
        _hotkeys.HotkeyPressed += OnHotkey;        // raised on the UI thread (WM_HOTKEY)
        _hotkeys.ToggleRequested += ToggleEnabled; // raised on the UI thread (WM_HOTKEY)

        _server = new WsServer(_cfg.Port);
        WireServer(_server);

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = "PlayControl Agent",
        };
        _tray.DoubleClick += (s, e) => ShowStatus();
        BuildMenu();

        StartServices();
    }

    // Marshal a callback onto the UI thread. Server events fire on thread-pool
    // threads (async read loops); WinForms timers and Show() must run on the
    // thread that owns the message loop, so we route through the overlay's
    // handle. This replaces the previously-captured SynchronizationContext,
    // which was grabbed before any control existed and therefore posted to the
    // thread pool (the cause of the overlay never auto-hiding).
    void Post(Action action)
    {
        try
        {
            if (_overlay.IsHandleCreated)
                _overlay.BeginInvoke(action);
            else
                action();
        }
        catch
        {
            // overlay disposed mid-shutdown; ignore
        }
    }

    void WireServer(WsServer server)
    {
        server.ConnectionChanged += conn => Post(() =>
        {
            _connected = conn;
            UpdateTrayText();
        });
        server.ResultReceived += text => Post(() => OnResult(text));
        server.ShortcutsReceived += map => Post(() => OnShortcutsReceived(map));
        server.ServerError += msg => Post(() =>
        {
            _tray.ShowBalloonTip(4000, "PlayControl Agent", msg, ToolTipIcon.Warning);
        });
    }

    // ---- lifecycle ---------------------------------------------------------

    void StartServices()
    {
        _server.Start();
        _hotkeys.RegisterToggle(_cfg.ToggleHotkey);
        if (_enabled)
        {
            _hotkeys.RegisterShortcuts(_shortcuts);
            ReportHotkeyErrors();
        }
        UpdateTrayText();
    }

    void ReportHotkeyErrors()
    {
        if (_hotkeys.Errors.Count > 0)
        {
            _tray.ShowBalloonTip(
                5000,
                "PlayControl Agent",
                string.Join("\n", _hotkeys.Errors),
                ToolTipIcon.Warning);
        }
    }

    void ReloadConfig()
    {
        _cfg = AppConfig.Load();
        _overlay.ApplyConfig(_cfg);

        // Restart the server if the port changed.
        _server.Stop();
        _server = new WsServer(_cfg.Port);
        WireServer(_server);
        _server.Start();

        _hotkeys.RegisterToggle(_cfg.ToggleHotkey);
        if (_enabled)
        {
            _hotkeys.RegisterShortcuts(_shortcuts);
            ReportHotkeyErrors();
        }
        UpdateTrayText();
        _tray.ShowBalloonTip(2500, "PlayControl Agent", "Configuration reloaded.", ToolTipIcon.Info);
    }

    // Shortcuts pushed from the extension: cache them and (re)register globally.
    void OnShortcutsReceived(Dictionary<string, List<string>> map)
    {
        _shortcuts = map;
        AppConfig.SaveSyncedShortcuts(map);
        if (_enabled)
        {
            _hotkeys.RegisterShortcuts(_shortcuts);
            ReportHotkeyErrors();
        }
        UpdateTrayText();
    }

    void ToggleEnabled()
    {
        _enabled = !_enabled;
        if (_enabled)
        {
            _hotkeys.RegisterShortcuts(_shortcuts);
            ReportHotkeyErrors();
            _overlay.Flash("PlayControl \u2014 hotkeys ON");
        }
        else
        {
            _hotkeys.UnregisterActions();
            _overlay.Flash("PlayControl \u2014 hotkeys OFF");
        }
        BuildMenu();
        UpdateTrayText();
    }

    // ---- hotkey + result handling -----------------------------------------

    void OnHotkey(string action)
    {
        // When connected, wait for the extension's result so the overlay shows
        // the real player state (and is suppressed if the browser is focused).
        // When not connected, give immediate local feedback.
        if (!_connected)
        {
            var label = ActionLabels.TryGetValue(action, out var l) ? l : action;
            _overlay.Flash(label + "  \u2014 browser not connected");
        }
        _ = _server.SendCommand(action);
    }

    // Skip the local overlay when the browser is the foreground window: the
    // in-page OSD already shows feedback there, so avoid a duplicate.
    void MaybeFlash(string text)
    {
        if (_cfg.SuppressOverlayWhenBrowserFocused && IsBrowserForeground())
            return;
        _overlay.Flash(text);
    }

    void OnResult(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String &&
                t.GetString() != "result")
                return;

            string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            bool ok = !root.TryGetProperty("ok", out var okEl) || okEl.GetBoolean();
            string label = ActionLabels.TryGetValue(action, out var l) ? l : action;

            root.TryGetProperty("info", out var info);

            if (!ok)
            {
                string reason = info.ValueKind == JsonValueKind.String ? info.GetString() ?? "" : "";
                string msg = reason switch
                {
                    "no-youtube-tab" => "no YouTube tab",
                    "tab-unreachable" => "reload the YouTube tab",
                    _ => "no YouTube tab",
                };
                MaybeFlash($"{label}  \u2014 {msg}");
                return;
            }

            // On success, info is an object: { title, currentTime, paused, ... }.
            string suffix = "";
            if (info.ValueKind == JsonValueKind.Object &&
                info.TryGetProperty("currentTime", out var ct) &&
                ct.ValueKind == JsonValueKind.Number)
            {
                suffix = "  " + FormatTime(ct.GetDouble());
            }
            MaybeFlash(label + suffix);
        }
        catch
        {
            // ignore malformed result messages
        }
    }

    static bool IsBrowserForeground()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return false;
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName.ToLowerInvariant();
            return name.Contains("firefox") || name.Contains("chrome") ||
                   name.Contains("msedge") || name.Contains("librewolf") ||
                   name.Contains("waterfox") || name.Contains("floorp");
        }
        catch
        {
            return false;
        }
    }

    static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    static Icon LoadAppIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name != null)
            {
                using var s = asm.GetManifestResourceStream(name);
                if (s != null) return new Icon(s);
            }
        }
        catch
        {
            // fall through
        }
        return SystemIcons.Application;
    }

    // ---- tray menu ---------------------------------------------------------

    void BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem(_enabled ? "Disable hotkeys" : "Enable hotkeys", null,
            (s, e) => ToggleEnabled()));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Edit configuration\u2026", null, (s, e) => OpenConfig()));
        menu.Items.Add(new ToolStripMenuItem("Reload configuration", null, (s, e) => ReloadConfig()));
        menu.Items.Add(new ToolStripMenuItem("Show status", null, (s, e) => ShowStatus()));

        var autostart = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart())
        {
            Checked = IsAutostartEnabled(),
            CheckOnClick = false,
        };
        menu.Items.Add(autostart);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApp()));

        _tray.ContextMenuStrip = menu;
    }

    void UpdateTrayText()
    {
        string state = !_enabled ? "hotkeys off" : _connected ? "browser connected" : "waiting for browser";
        _tray.Text = $"PlayControl Agent \u2014 {state} (port {_cfg.Port})";
    }

    void ShowStatus()
    {
        string state = !_enabled
            ? "Hotkeys are disabled."
            : _connected
                ? "Browser extension is connected."
                : "Enabled \u2014 waiting for the browser extension to connect.";

        var lines = _shortcuts
            .Where(kv => kv.Value != null && kv.Value.Any(c => !string.IsNullOrWhiteSpace(c)))
            .Select(kv =>
            {
                var label = ActionLabels.TryGetValue(kv.Key, out var l) ? l : kv.Key;
                return $"  {label}  \u2192  {string.Join(", ", kv.Value.Where(c => !string.IsNullOrWhiteSpace(c)))}";
            })
            .ToList();
        string keys = lines.Count == 0
            ? "No hotkeys synced yet (configure them in the extension and connect)."
            : string.Join("\n", lines);

        MessageBox.Show(
            $"{state}\n\nPort: {_cfg.Port}\nMaster toggle: {_cfg.ToggleHotkey}\n\n" +
            $"Global hotkeys (synced from the extension):\n{keys}\n\nConfig file:\n{AppConfig.ConfigPath}",
            "PlayControl Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    void OpenConfig()
    {
        try
        {
            if (!File.Exists(AppConfig.ConfigPath))
                _cfg.Save();
            Process.Start(new ProcessStartInfo
            {
                FileName = AppConfig.ConfigPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open config file: " + ex.Message, "PlayControl Agent");
        }
    }

    // ---- autostart ---------------------------------------------------------

    static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(RunValue) != null;
        }
        catch
        {
            return false;
        }
    }

    void ToggleAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (IsAutostartEnabled())
            {
                key.DeleteValue(RunValue, false);
            }
            else
            {
                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(RunValue, $"\"{exe}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not change autostart setting: " + ex.Message, "PlayControl Agent");
        }
        BuildMenu();
    }

    // ---- exit --------------------------------------------------------------

    void ExitApp()
    {
        try { _hotkeys.Dispose(); } catch { /* ignore */ }
        try { _server.Stop(); } catch { /* ignore */ }
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        ExitThread();
    }
}
