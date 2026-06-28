using System.Runtime.InteropServices;

namespace PlayControlAgent;

// Registers system-wide hotkeys via the Win32 RegisterHotKey API and raises an
// event (carrying the mapped action name) when one is pressed. Uses a hidden
// message-only window to receive WM_HOTKEY.
public class HotkeyManager : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    enum Mods : uint { Alt = 1, Ctrl = 2, Shift = 4, Win = 8, NoRepeat = 0x4000 }

    readonly Dictionary<int, string> _idToAction = new();
    int _nextId = 1;

    public event Action<string>? HotkeyPressed;
    public List<string> Errors { get; } = new();

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public void RegisterAll(Dictionary<string, string> hotkeys)
    {
        UnregisterAll();
        Errors.Clear();
        foreach (var kv in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            if (!TryParse(kv.Key, out uint mods, out uint vk))
            {
                Errors.Add($"Unrecognized hotkey: \"{kv.Key}\"");
                continue;
            }
            int id = _nextId++;
            if (RegisterHotKey(Handle, id, mods | (uint)Mods.NoRepeat, vk))
                _idToAction[id] = kv.Value;
            else
                Errors.Add($"Could not register \"{kv.Key}\" (already in use by another app?)");
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToAction.Keys)
            UnregisterHotKey(Handle, id);
        _idToAction.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (_idToAction.TryGetValue(id, out var action))
                HotkeyPressed?.Invoke(action);
        }
        base.WndProc(ref m);
    }

    // ---- parsing -----------------------------------------------------------

    static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? keyTok = null;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= (uint)Mods.Ctrl;
                    break;
                case "alt":
                    mods |= (uint)Mods.Alt;
                    break;
                case "shift":
                    mods |= (uint)Mods.Shift;
                    break;
                case "win":
                case "meta":
                case "super":
                case "cmd":
                    mods |= (uint)Mods.Win;
                    break;
                default:
                    keyTok = p;
                    break;
            }
        }
        if (keyTok == null) return false;
        return TryKey(keyTok, out vk);
    }

    static readonly Dictionary<string, Keys> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = Keys.Left,
        ["right"] = Keys.Right,
        ["up"] = Keys.Up,
        ["down"] = Keys.Down,
        ["home"] = Keys.Home,
        ["end"] = Keys.End,
        ["pageup"] = Keys.PageUp,
        ["pgup"] = Keys.PageUp,
        ["pagedown"] = Keys.PageDown,
        ["pgdn"] = Keys.PageDown,
        ["insert"] = Keys.Insert,
        ["ins"] = Keys.Insert,
        ["delete"] = Keys.Delete,
        ["del"] = Keys.Delete,
        ["space"] = Keys.Space,
        ["spacebar"] = Keys.Space,
        ["enter"] = Keys.Enter,
        ["return"] = Keys.Enter,
        ["tab"] = Keys.Tab,
        ["escape"] = Keys.Escape,
        ["esc"] = Keys.Escape,
        ["backspace"] = Keys.Back,
        ["pause"] = Keys.Pause,
        ["break"] = Keys.Pause,
    };

    static bool TryKey(string k, out uint vk)
    {
        vk = 0;
        k = k.Trim();
        if (k.Length == 0) return false;
        var n = k.ToLowerInvariant();

        if (NamedKeys.TryGetValue(n, out var named))
        {
            vk = (uint)named;
            return true;
        }
        // F1..F24
        if (n.Length >= 2 && n[0] == 'f' && int.TryParse(n.AsSpan(1), out int fn) && fn >= 1 && fn <= 24)
        {
            vk = (uint)(Keys.F1 + (fn - 1));
            return true;
        }
        // Numpad0..Numpad9
        if (n.StartsWith("numpad") && int.TryParse(n.AsSpan(6), out int np) && np >= 0 && np <= 9)
        {
            vk = (uint)(Keys.NumPad0 + np);
            return true;
        }
        // single digit
        if (n.Length == 1 && n[0] >= '0' && n[0] <= '9')
        {
            vk = (uint)(Keys.D0 + (n[0] - '0'));
            return true;
        }
        // single letter
        if (n.Length == 1 && n[0] >= 'a' && n[0] <= 'z')
        {
            vk = (uint)(Keys.A + (n[0] - 'a'));
            return true;
        }
        // fallback: Keys enum name
        if (Enum.TryParse<Keys>(k, true, out var parsed))
        {
            vk = (uint)parsed;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }
}
