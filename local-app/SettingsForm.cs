using System.Drawing;

namespace PlayControlAgent;

// A small settings window opened from the tray menu. Lets the user configure
// the OSD overlay, the master toggle hotkey, port and autostart, and shows the
// live status of every global hotkey synced from the extension. The action
// hotkeys themselves are configured in the browser extension (single source of
// truth) and are only displayed here.
//
// Layout uses Dock/Anchor and TableLayoutPanel so the form scales correctly at
// any Windows DPI setting (100 %, 125 %, 150 %, …).
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
        Text = "PlayControl Agent \u2014 Settings";
        Icon = icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 500);
        Font = new Font("Segoe UI", 9f);

        // Bottom button panel (fixed height, docked to bottom).
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8, 6, 8, 6),
        };
        var cancel = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Size = new Size(84, 32) };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Size = new Size(84, 32) };
        save.Click += (s, e) => OnSave();
        btnPanel.Controls.Add(cancel);
        btnPanel.Controls.Add(save);
        Controls.Add(btnPanel);
        AcceptButton = save;
        CancelButton = cancel;

        // Tab control fills the rest.
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildOverlayTab());
        tabs.TabPages.Add(BuildStatusTab());
        Controls.Add(tabs);

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
        var page = new TabPage("General") { Padding = new Padding(12) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Row 0: port label
        var portLabel = new Label
        {
            Text = "WebSocket port (must match the extension):",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        layout.Controls.Add(portLabel, 0, 0);
        layout.SetColumnSpan(portLabel, 2);

        // Row 1: port input
        _port = new NumericUpDown
        {
            Width = 110,
            Minimum = 1,
            Maximum = 65535,
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.Controls.Add(_port, 0, 1);

        // Row 2: toggle label
        var toggleLabel = new Label
        {
            Text = "Master toggle hotkey (enables / disables all global hotkeys):",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 4, 0, 4),
        };
        layout.Controls.Add(toggleLabel, 0, 2);
        layout.SetColumnSpan(toggleLabel, 2);

        // Row 3: toggle input + clear button
        var toggleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2),
        };
        _toggle = new HotkeyBox
        {
            Width = 220,
            ReadOnly = true,
            Margin = new Padding(0, 0, 8, 0),
        };
        _toggle.ComboChanged += (s, e) => UpdateToggleValidity();
        var clearToggle = new Button
        {
            Text = "Clear",
            Size = new Size(72, 28),
        };
        clearToggle.Click += (s, e) => { _toggle.Combo = ""; UpdateToggleValidity(); };
        toggleRow.Controls.Add(_toggle);
        toggleRow.Controls.Add(clearToggle);
        layout.Controls.Add(toggleRow, 0, 3);
        layout.SetColumnSpan(toggleRow, 2);

        // Row 4: toggle status label
        _toggleStatus = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = Color.Gray,
            Margin = new Padding(0, 0, 0, 20),
            Text = "Click the box and press a key combination.",
        };
        layout.Controls.Add(_toggleStatus, 0, 4);
        layout.SetColumnSpan(_toggleStatus, 2);

        // Row 5: autostart
        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        layout.Controls.Add(_autostart, 0, 5);
        layout.SetColumnSpan(_autostart, 2);

        page.Controls.Add(layout);
        return page;
    }

    TabPage BuildOverlayTab()
    {
        var page = new TabPage("Overlay (OSD)") { Padding = new Padding(12) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Row 0: show overlay checkbox
        _showOverlay = new CheckBox
        {
            Text = "Show on-screen overlay when controlling video",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        _showOverlay.CheckedChanged += (s, e) => UpdateOverlayEnabled();
        layout.Controls.Add(_showOverlay, 0, row);
        layout.SetColumnSpan(_showOverlay, 2);
        row++;

        // Row 1: suppress when browser
        _suppressWhenBrowser = new CheckBox
        {
            Text = "Hide overlay while the controlled tab is visible in the browser",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.Controls.Add(_suppressWhenBrowser, 0, row);
        layout.SetColumnSpan(_suppressWhenBrowser, 2);
        row++;

        // Row 2: corner
        layout.Controls.Add(MakeFieldLabel("Corner:"), 0, row);
        _corner = new ComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 2, 0, 6),
        };
        _corner.Items.AddRange(new object[] { "top-right", "top-left", "bottom-right", "bottom-left" });
        layout.Controls.Add(_corner, 1, row);
        row++;

        // Row 3: font size
        layout.Controls.Add(MakeFieldLabel("Font size:"), 0, row);
        _fontSize = new NumericUpDown
        {
            Width = 80,
            Minimum = 10,
            Maximum = 96,
            Margin = new Padding(0, 2, 0, 6),
        };
        layout.Controls.Add(_fontSize, 1, row);
        row++;

        // Row 4: duration
        layout.Controls.Add(MakeFieldLabel("Duration (ms):"), 0, row);
        _duration = new NumericUpDown
        {
            Width = 100,
            Minimum = 300,
            Maximum = 10000,
            Increment = 100,
            Margin = new Padding(0, 2, 0, 6),
        };
        layout.Controls.Add(_duration, 1, row);
        row++;

        // Row 5: color
        layout.Controls.Add(MakeFieldLabel("Color:"), 0, row);
        var colorRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16),
        };
        _colorSwatch = new Panel
        {
            Size = new Size(40, 26),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 2, 8, 0),
        };
        _colorBtn = new Button { Text = "Choose\u2026", Size = new Size(90, 28) };
        _colorBtn.Click += (s, e) => PickColor();
        colorRow.Controls.Add(_colorSwatch);
        colorRow.Controls.Add(_colorBtn);
        layout.Controls.Add(colorRow, 1, row);
        row++;

        // Row 6: preview button
        var preview = new Button
        {
            Text = "Preview overlay",
            Size = new Size(140, 32),
            Margin = new Padding(0, 4, 0, 0),
        };
        preview.Click += (s, e) => _ctx.PreviewOverlay(BuildConfigFromControls());
        layout.Controls.Add(preview, 0, row);
        layout.SetColumnSpan(preview, 2);

        page.Controls.Add(layout);
        return page;
    }

    TabPage BuildStatusTab()
    {
        var page = new TabPage("Hotkey status") { Padding = new Padding(12) };
        _connState = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        };
        page.Controls.Add(_connState);

        _statusList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };
        _statusList.Columns.Add("Action", 160);
        _statusList.Columns.Add("Key", 150);
        _statusList.Columns.Add("Status", 140);
        page.Controls.Add(_statusList);

        // Dock order matters: add list first, then label, so label docks on top.
        page.Controls.SetChildIndex(_connState, 0);

        return page;
    }

    static Label MakeFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 12, 4),
    };

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
            _toggleStatus.Text = "No master toggle set \u2014 you can still use the tray menu to enable/disable.";
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
                ? "Enabled \u2014 browser extension connected."
                : "Enabled \u2014 waiting for the browser extension to connect.";
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
