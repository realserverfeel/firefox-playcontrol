using System.Drawing;

namespace PlayControlAgent;

// A small settings window opened from the tray menu. Lets the user configure
// the OSD overlay, the master toggle hotkey, port and autostart, and shows the
// live status of every global hotkey synced from the extension. The action
// hotkeys themselves are configured in the browser extension (single source of
// truth) and are only displayed here.
public class SettingsForm : Form
{
    readonly TrayAppContext _ctx;

    // ---- general tab controls ----
    NumericUpDown _port = null!;
    HotkeyBox _toggle = null!;
    Label _toggleStatus = null!;
    CheckBox _autostart = null!;

    // ---- overlay tab controls ----
    CheckBox _showOverlay = null!;
    CheckBox _suppressWhenBrowser = null!;
    ComboBox _corner = null!;
    NumericUpDown _fontSize = null!;
    NumericUpDown _duration = null!;
    Button _colorBtn = null!;
    Panel _colorSwatch = null!;
    string _color = "#3b9dff";

    // ---- status tab controls ----
    Label _connState = null!;
    ListView _statusList = null!;
    System.Windows.Forms.Timer _statusTimer = null!;

    public SettingsForm(TrayAppContext ctx, Icon icon)
    {
        _ctx = ctx;
        Text = "PlayControl Agent — Settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 460);
        Font = new Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Top, Height = 400 };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildOverlayTab());
        tabs.TabPages.Add(BuildStatusTab());
        Controls.Add(tabs);

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK };
        save.Location = new Point(ClientSize.Width - 180, 414);
        save.Size = new Size(80, 30);
        save.Click += (s, e) => OnSave();

        var cancel = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
        cancel.Location = new Point(ClientSize.Width - 92, 414);
        cancel.Size = new Size(80, 30);

        Controls.Add(save);
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;

        LoadFromConfig(_ctx.CurrentConfig);
        RefreshStatus();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (s, e) => RefreshStatus();
        _statusTimer.Start();
        FormClosed += (s, e) => _statusTimer.Stop();
    }

    // ---- tab construction --------------------------------------------------

    TabPage BuildGeneralTab()
    {
        var page = new TabPage("General");
        int y = 18;

        page.Controls.Add(MakeLabel("WebSocket port (must match the extension):", 16, y));
        y += 22;
        _port = new NumericUpDown
        {
            Location = new Point(18, y),
            Width = 100,
            Minimum = 1,
            Maximum = 65535,
        };
        page.Controls.Add(_port);
        y += 40;

        page.Controls.Add(MakeLabel("Master toggle hotkey (enables/disables all global hotkeys):", 16, y));
        y += 22;
        _toggle = new HotkeyBox { Location = new Point(18, y), Width = 220, ReadOnly = true };
        _toggle.ComboChanged += (s, e) => UpdateToggleValidity();
        page.Controls.Add(_toggle);

        var clearToggle = new Button { Text = "Clear", Location = new Point(246, y - 1), Size = new Size(70, 26) };
        clearToggle.Click += (s, e) => { _toggle.Combo = ""; UpdateToggleValidity(); };
        page.Controls.Add(clearToggle);
        y += 26;
        _toggleStatus = new Label
        {
            Location = new Point(18, y),
            Size = new Size(420, 18),
            ForeColor = Color.Gray,
            Text = "Click the box and press a key combination.",
        };
        page.Controls.Add(_toggleStatus);
        y += 40;

        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(18, y),
            AutoSize = true,
        };
        page.Controls.Add(_autostart);

        return page;
    }

    TabPage BuildOverlayTab()
    {
        var page = new TabPage("Overlay (OSD)");
        int y = 18;

        _showOverlay = new CheckBox
        {
            Text = "Show on-screen overlay when controlling video",
            Location = new Point(18, y),
            AutoSize = true,
        };
        _showOverlay.CheckedChanged += (s, e) => UpdateOverlayEnabled();
        page.Controls.Add(_showOverlay);
        y += 28;

        _suppressWhenBrowser = new CheckBox
        {
            Text = "Hide overlay while the browser is focused (in-page OSD shows instead)",
            Location = new Point(18, y),
            AutoSize = true,
        };
        page.Controls.Add(_suppressWhenBrowser);
        y += 40;

        page.Controls.Add(MakeLabel("Corner:", 16, y + 4));
        _corner = new ComboBox
        {
            Location = new Point(120, y),
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _corner.Items.AddRange(new object[] { "top-right", "top-left", "bottom-right", "bottom-left" });
        page.Controls.Add(_corner);
        y += 34;

        page.Controls.Add(MakeLabel("Font size:", 16, y + 4));
        _fontSize = new NumericUpDown
        {
            Location = new Point(120, y),
            Width = 80,
            Minimum = 10,
            Maximum = 96,
        };
        page.Controls.Add(_fontSize);
        y += 34;

        page.Controls.Add(MakeLabel("Duration (ms):", 16, y + 4));
        _duration = new NumericUpDown
        {
            Location = new Point(120, y),
            Width = 90,
            Minimum = 300,
            Maximum = 10000,
            Increment = 100,
        };
        page.Controls.Add(_duration);
        y += 34;

        page.Controls.Add(MakeLabel("Color:", 16, y + 4));
        _colorSwatch = new Panel
        {
            Location = new Point(120, y),
            Size = new Size(40, 26),
            BorderStyle = BorderStyle.FixedSingle,
        };
        page.Controls.Add(_colorSwatch);
        _colorBtn = new Button { Text = "Choose…", Location = new Point(168, y - 1), Size = new Size(90, 28) };
        _colorBtn.Click += (s, e) => PickColor();
        page.Controls.Add(_colorBtn);
        y += 40;

        var preview = new Button { Text = "Preview overlay", Location = new Point(18, y), Size = new Size(140, 30) };
        preview.Click += (s, e) => _ctx.PreviewOverlay(BuildConfigFromControls());
        page.Controls.Add(preview);

        return page;
    }

    TabPage BuildStatusTab()
    {
        var page = new TabPage("Hotkey status");
        _connState = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(420, 36),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        page.Controls.Add(_connState);

        _statusList = new ListView
        {
            Location = new Point(12, 52),
            Size = new Size(412, 300),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };
        _statusList.Columns.Add("Action", 150);
        _statusList.Columns.Add("Key", 130);
        _statusList.Columns.Add("Status", 120);
        page.Controls.Add(_statusList);

        return page;
    }

    static Label MakeLabel(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    // ---- load / save -------------------------------------------------------

    void LoadFromConfig(AppConfig c)
    {
        _port.Value = Math.Clamp(c.Port, 1, 65535);
        _toggle.Combo = c.ToggleHotkey ?? "";
        _autostart.Checked = _ctx.AutostartEnabled;

        _showOverlay.Checked = c.ShowOverlay;
        _suppressWhenBrowser.Checked = c.SuppressOverlayWhenBrowserFocused;
        _corner.SelectedItem = c.OverlayCorner;
        if (_corner.SelectedIndex < 0) _corner.SelectedItem = "top-right";
        _fontSize.Value = Math.Clamp(c.OverlayFontSize, 10, 96);
        _duration.Value = Math.Clamp(c.OverlayDurationMs, 300, 10000);
        SetColor(c.OverlayColor);

        UpdateOverlayEnabled();
        UpdateToggleValidity();
    }

    AppConfig BuildConfigFromControls()
    {
        var c = _ctx.CurrentConfig; // preserves legacy fields
        c.Port = (int)_port.Value;
        c.ToggleHotkey = _toggle.Combo.Trim();
        c.ShowOverlay = _showOverlay.Checked;
        c.SuppressOverlayWhenBrowserFocused = _suppressWhenBrowser.Checked;
        c.OverlayCorner = _corner.SelectedItem?.ToString() ?? "top-right";
        c.OverlayFontSize = (int)_fontSize.Value;
        c.OverlayDurationMs = (int)_duration.Value;
        c.OverlayColor = _color;
        return c;
    }

    void OnSave()
    {
        var combo = _toggle.Combo.Trim();
        if (combo.Length > 0 && !HotkeyManager.IsValidCombo(combo))
        {
            MessageBox.Show(this,
                "The master toggle hotkey is not a recognized key combination.",
                "PlayControl Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        _ctx.ApplySettings(BuildConfigFromControls());
        _ctx.SetAutostart(_autostart.Checked);
        RefreshStatus();
    }

    // ---- helpers -----------------------------------------------------------

    void UpdateOverlayEnabled()
    {
        bool on = _showOverlay.Checked;
        _suppressWhenBrowser.Enabled = on;
        _corner.Enabled = on;
        _fontSize.Enabled = on;
        _duration.Enabled = on;
        _colorBtn.Enabled = on;
    }

    void UpdateToggleValidity()
    {
        var combo = _toggle.Combo.Trim();
        if (combo.Length == 0)
        {
            _toggleStatus.ForeColor = Color.Gray;
            _toggleStatus.Text = "No master toggle set — you can still use the tray menu to enable/disable.";
        }
        else if (HotkeyManager.IsValidCombo(combo))
        {
            _toggleStatus.ForeColor = Color.SeaGreen;
            _toggleStatus.Text = $"\u2713 {combo}";
        }
        else
        {
            _toggleStatus.ForeColor = Color.Firebrick;
            _toggleStatus.Text = $"\u26A0 \"{combo}\" is not a recognized key.";
        }
    }

    void PickColor()
    {
        using var dlg = new ColorDialog { FullOpen = true };
        try { dlg.Color = ColorTranslator.FromHtml(_color); } catch { /* ignore */ }
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SetColor(ColorTranslator.ToHtml(dlg.Color));
    }

    void SetColor(string hex)
    {
        _color = string.IsNullOrWhiteSpace(hex) ? "#3b9dff" : hex;
        try { _colorSwatch.BackColor = ColorTranslator.FromHtml(_color); }
        catch { _colorSwatch.BackColor = Color.DodgerBlue; }
    }

    void RefreshStatus()
    {
        bool connected = _ctx.IsConnected;
        bool enabled = _ctx.IsEnabled;
        _connState.Text = !enabled
            ? "Hotkeys are DISABLED (master toggle is off)."
            : connected
                ? "Enabled — browser extension connected."
                : "Enabled — waiting for the browser extension to connect.";
        _connState.ForeColor = !enabled ? Color.Firebrick : connected ? Color.SeaGreen : Color.DarkGoldenrod;

        _statusList.BeginUpdate();
        _statusList.Items.Clear();

        var toggle = _ctx.ToggleHotkeyStatus;
        if (toggle != null)
            AddStatusRow("Master toggle", toggle.Combo, enabled ? toggle.Ok : (bool?)null, toggle.Error, enabled);

        var rows = _ctx.ActionHotkeyStatus;
        if (rows.Count == 0)
        {
            var item = new ListViewItem(new[]
            {
                "(none synced)", "", "configure in extension",
            }) { ForeColor = Color.Gray };
            _statusList.Items.Add(item);
        }
        else
        {
            foreach (var r in rows)
                AddStatusRow(TrayAppContext.Label(r.Action), r.Combo, enabled ? r.Ok : (bool?)null, r.Error, enabled);
        }
        _statusList.EndUpdate();
    }

    void AddStatusRow(string action, string combo, bool? ok, string error, bool enabled)
    {
        string statusText;
        Color color;
        if (!enabled)
        {
            statusText = "off";
            color = Color.Gray;
        }
        else if (ok == true)
        {
            statusText = "\u2713 active";
            color = Color.SeaGreen;
        }
        else
        {
            statusText = "\u26A0 " + (string.IsNullOrEmpty(error) ? "failed" : error);
            color = Color.Firebrick;
        }
        var item = new ListViewItem(new[] { action, combo, statusText }) { ForeColor = color };
        _statusList.Items.Add(item);
    }

    // A read-only text box that captures a key combination and renders it in
    // the same format the extension uses (e.g. "Ctrl+Alt+P", "Home", "F8").
    class HotkeyBox : TextBox
    {
        string _combo = "";

        public event EventHandler? ComboChanged;

        public string Combo
        {
            get => _combo;
            set
            {
                _combo = value ?? "";
                Text = _combo.Length == 0 ? "(none)" : _combo;
                ComboChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public HotkeyBox()
        {
            Cursor = Cursors.Hand;
            Text = "(none)";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Capture everything (including Tab, arrows) while focused.
            if (Focused)
            {
                var combo = ComboFromKeyData(keyData);
                if (combo != null)
                {
                    Combo = combo;
                    return true;
                }
                // lone modifier press: swallow so focus doesn't move
                if (IsModifier(keyData & Keys.KeyCode)) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static bool IsModifier(Keys k) =>
            k is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
              or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
              or Keys.LMenu or Keys.RMenu;

        static string? ComboFromKeyData(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.None || IsModifier(key)) return null;

            var parts = new List<string>();
            if ((keyData & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            if ((keyData & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            parts.Add(KeyToken(key));
            return string.Join("+", parts);
        }

        static string KeyToken(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Numpad" + (k - Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
            return k.ToString(); // Home, End, Insert, Delete, PageUp, PageDown, Left, …
        }
    }
}
