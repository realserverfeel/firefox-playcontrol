using System.Drawing;
using System.Runtime.InteropServices;

namespace PlayControlAgent;

// A borderless, topmost, click-through on-screen overlay that flashes a short
// blue text in a screen corner (PotPlayer-style), then fades out. Used to give
// feedback when a global hotkey fires while another window is focused.
public class OverlayForm : Form
{
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_TOOLWINDOW = 0x80;
    const int WS_EX_NOACTIVATE = 0x08000000;

    readonly Label _label;
    readonly System.Windows.Forms.Timer _hideTimer;
    AppConfig _cfg;

    public OverlayForm(AppConfig cfg)
    {
        _cfg = cfg;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TransparencyKey = Color.Black; // black pixels become fully transparent
        AutoSize = false;
        DoubleBuffered = true;

        _label = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", _cfg.OverlayFontSize, FontStyle.Bold),
            ForeColor = ColorFromHex(_cfg.OverlayColor, Color.FromArgb(0x3b, 0x9d, 0xff)),
            BackColor = Color.Transparent,
            Location = new Point(12, 8),
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = Math.Max(300, _cfg.OverlayDurationMs) };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public void ApplyConfig(AppConfig cfg)
    {
        _cfg = cfg;
        _label.Font = new Font("Segoe UI", _cfg.OverlayFontSize, FontStyle.Bold);
        _label.ForeColor = ColorFromHex(_cfg.OverlayColor, Color.FromArgb(0x3b, 0x9d, 0xff));
        _hideTimer.Interval = Math.Max(300, _cfg.OverlayDurationMs);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void Flash(string text)
    {
        if (!_cfg.ShowOverlay) return;
        _label.Text = text;
        var sz = _label.PreferredSize;
        Size = new Size(sz.Width + 24, sz.Height + 16);
        PositionToCorner();
        if (!Visible) Show();
        TopMost = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    // Show on whichever monitor the user is actually looking at: the screen
    // containing the foreground window, falling back to the cursor's screen and
    // then the primary screen.
    static Rectangle ActiveWorkingArea()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h != IntPtr.Zero)
                return Screen.FromHandle(h).WorkingArea;
        }
        catch
        {
            // fall through
        }
        try { return Screen.FromPoint(Cursor.Position).WorkingArea; }
        catch { return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720); }
    }

    void PositionToCorner()
    {
        var wa = ActiveWorkingArea();
        const int margin = 40;
        int x, y;
        switch ((_cfg.OverlayCorner ?? "top-right").ToLowerInvariant())
        {
            case "top-left":
                x = wa.Left + margin;
                y = wa.Top + margin;
                break;
            case "bottom-right":
                x = wa.Right - Width - margin;
                y = wa.Bottom - Height - margin;
                break;
            case "bottom-left":
                x = wa.Left + margin;
                y = wa.Bottom - Height - margin;
                break;
            default: // top-right
                x = wa.Right - Width - margin;
                y = wa.Top + margin;
                break;
        }
        Location = new Point(x, y);
    }

    static Color ColorFromHex(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return ColorTranslator.FromHtml(hex.Trim());
        }
        catch
        {
            // ignore
        }
        return fallback;
    }
}
