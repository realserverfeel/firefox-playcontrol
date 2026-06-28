using System.Diagnostics;
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
    WsServer _server;
    readonly SynchronizationContext _ui;
    bool _connected;
    bool _enabled = true;

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
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _cfg = AppConfig.Load();

        _overlay = new OverlayForm(_cfg);
        // Force handle creation so the first Flash is instant.
        _ = _overlay.Handle;

        _hotkeys = new HotkeyManager();
        _hotkeys.HotkeyPressed += OnHotkey;

        _server = new WsServer(_cfg.Port);
        WireServer(_server);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "PlayControl Agent",
        };
        _tray.DoubleClick += (s, e) => ShowStatus();
        BuildMenu();

        StartServices();
    }

    void WireServer(WsServer server)
    {
        server.ConnectionChanged += conn => _ui.Post(_ =>
        {
            _connected = conn;
            UpdateTrayText();
        }, null);
        server.ResultReceived += text => _ui.Post(_ => OnResult(text), null);
        server.ServerError += msg => _ui.Post(_ =>
        {
            _tray.ShowBalloonTip(4000, "PlayControl Agent", msg, ToolTipIcon.Warning);
        }, null);
    }

    // ---- lifecycle ---------------------------------------------------------

    void StartServices()
    {
        _server.Start();
        if (_enabled)
        {
            _hotkeys.RegisterAll(_cfg.Hotkeys);
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

        if (_enabled)
        {
            _hotkeys.RegisterAll(_cfg.Hotkeys);
            ReportHotkeyErrors();
        }
        UpdateTrayText();
        _tray.ShowBalloonTip(2500, "PlayControl Agent", "Configuration reloaded.", ToolTipIcon.Info);
    }

    void ToggleEnabled()
    {
        _enabled = !_enabled;
        if (_enabled)
        {
            _hotkeys.RegisterAll(_cfg.Hotkeys);
            ReportHotkeyErrors();
        }
        else
        {
            _hotkeys.UnregisterAll();
        }
        BuildMenu();
        UpdateTrayText();
    }

    // ---- hotkey + result handling -----------------------------------------

    void OnHotkey(string action)
    {
        _ui.Post(_ =>
        {
            var label = ActionLabels.TryGetValue(action, out var l) ? l : action;
            if (!_connected)
                _overlay.Flash(label + "  \u2014 browser not connected");
            else
                _overlay.Flash(label);
        }, null);
        _ = _server.SendCommand(action);
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
                _overlay.Flash($"{label}  \u2014 {msg}");
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
            _overlay.Flash(label + suffix);
        }
        catch
        {
            // ignore malformed result messages
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
        string keys = _cfg.Hotkeys.Count == 0
            ? "No hotkeys configured."
            : string.Join("\n", _cfg.Hotkeys.Select(kv => $"  {kv.Key}  \u2192  {kv.Value}"));
        MessageBox.Show(
            $"{state}\n\nPort: {_cfg.Port}\n\nHotkeys:\n{keys}\n\nConfig file:\n{AppConfig.ConfigPath}",
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
