using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace PlayControlAgent;

static class Program
{
    static Mutex? _singleton;

    [STAThread]
    static void Main()
    {
        // Single-instance: a second launch exits so two agents don't fight over
        // the same WebSocket port and global hotkeys.
        _singleton = new Mutex(true, @"Global\PlayControlAgentSingleton_4f2a", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "PlayControl Agent is already running (see the system tray).",
                "PlayControl Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.SystemAware); } catch { /* ignore */ }
        try { Application.Run(new TrayAppContext()); }
        finally { _singleton.ReleaseMutex(); }
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
    SettingsForm? _settingsForm;

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
        _tray.DoubleClick += (s, e) => OpenSettings();
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
        ApplySettings(AppConfig.Load());
        _tray.ShowBalloonTip(2500, "PlayControl Agent", "Configuration reloaded.", ToolTipIcon.Info);
    }

    // ---- public surface used by the settings window ------------------------

    public AppConfig CurrentConfig => _cfg.Clone();
    public bool IsConnected => _connected;
    public bool IsEnabled => _enabled;
    public IReadOnlyList<HotkeyManager.HotkeyStatus> ActionHotkeyStatus => _hotkeys.Status;
    public HotkeyManager.HotkeyStatus? ToggleHotkeyStatus => _hotkeys.ToggleStatus;
    public bool AutostartEnabled => IsAutostartEnabled();
    public static string Label(string action) =>
        ActionLabels.TryGetValue(action, out var l) ? l : action;

    // Apply a new configuration in-memory, persist it, and re-apply anything
    // affected (overlay style, server port, master toggle hotkey).
    public void ApplySettings(AppConfig newCfg)
    {
        bool portChanged = newCfg.Port != _cfg.Port;
        bool toggleChanged = newCfg.ToggleHotkey != _cfg.ToggleHotkey;

        _cfg = newCfg;
        _cfg.Save();
        _overlay.ApplyConfig(_cfg);

        if (portChanged)
        {
            _connected = false;
            _server.Stop();
            _server = new WsServer(_cfg.Port);
            WireServer(_server);
            _server.Start();
        }
        if (toggleChanged)
            _hotkeys.RegisterToggle(_cfg.ToggleHotkey);

        UpdateTrayText();
    }

    // Temporarily apply overlay settings and flash a sample so the user can see
    // the effect before saving. The live config is re-applied when the window
    // closes.
    public void PreviewOverlay(AppConfig cfg)
    {
        _overlay.ApplyConfig(cfg);
        _overlay.Flash("PlayControl \u2014 preview  1:23");
    }

    public void SetAutostart(bool on)
    {
        if (on == IsAutostartEnabled()) return;
        ToggleAutostart();
    }

    void OpenSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(this, _appIcon);
        _settingsForm.FormClosed += (s, e) =>
        {
            // Restore the live overlay style (in case Preview changed it).
            _overlay.ApplyConfig(_cfg);
            _settingsForm = null;
        };
        _settingsForm.Show();
        _settingsForm.Activate();
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

    // Skip the local overlay only when the in-page OSD is actually visible to
    // the user: the browser is the foreground window AND the controlled tab is
    // the active tab there. If the user is on a different tab or another app,
    // the in-page OSD isn't visible, so we still show the local overlay.
    void MaybeFlash(string text, bool targetVisible)
    {
        if (_cfg.SuppressOverlayWhenBrowserFocused && targetVisible && IsBrowserForeground())
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
            bool visible = root.TryGetProperty("visible", out var visEl) &&
                           visEl.ValueKind == JsonValueKind.True;

            if (!ok)
            {
                string reason = info.ValueKind == JsonValueKind.String ? info.GetString() ?? "" : "";
                string msg = reason switch
                {
                    "no-youtube-tab" => "no YouTube tab",
                    "tab-unreachable" => "reload the YouTube tab",
                    _ => "no YouTube tab",
                };
                // Errors are never shown in-page, so always surface them.
                MaybeFlash($"{label}  \u2014 {msg}", false);
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
            MaybeFlash(label + suffix, visible);
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

        menu.Items.Add(new ToolStripMenuItem("Settings\u2026", null, (s, e) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("Edit config file\u2026", null, (s, e) => OpenConfig()));
        menu.Items.Add(new ToolStripMenuItem("Reload config file", null, (s, e) => ReloadConfig()));

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
