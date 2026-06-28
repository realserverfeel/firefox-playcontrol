# PlayControl Agent (optional local companion app)

A tiny Windows tray app that registers **system-wide global hotkeys** and
forwards them to the PlayControl Firefox extension over a loopback WebSocket.
This lets you control YouTube playback **while another window is focused**
(taking notes, reading a PDF, etc.).

> The extension works perfectly on its own. This app is a pure **add-on**: if
> it is not running, the extension's in-page shortcuts behave exactly as before.
> The extension only ever *tries* to connect to `127.0.0.1` and silently retries
> — it never blocks or errors when the app is absent.

## How it fits together

```
[Agent] --registers system hotkeys--> Windows
   |  WebSocket server on 127.0.0.1:8423
   v
[Firefox extension background] --controls--> the target YouTube tab's <video>
```

- The **app** is the WebSocket *server*; the **extension** is the *client*.
- **Hotkeys are configured once, in the extension.** On connect (and whenever
  you change them) the extension pushes its shortcut map
  (`{type:"shortcuts", shortcuts:{action:[combos]}}`) to the app, which then
  registers those exact keys as **system-wide global hotkeys** (PotPlayer
  style) — including bare keys like `Home`/`End`. The map is cached to
  `shortcuts.json` so the keys are re-registered on startup, before the
  browser connects.
- When a hotkey fires, the app sends `{type:"command", id, action}` to the
  extension; the extension runs the action on the target tab and replies with
  `{type:"result", id, action, ok, info}` so the app can show accurate overlay
  feedback.
- A **master toggle hotkey** (default `Ctrl+Alt+P`) enables/disables all the
  global hotkeys at once, so you can momentarily hand the keys back to other
  apps. It stays registered while the app runs.
- While the browser is the foreground window the app suppresses its own corner
  overlay (the in-page OSD already shows feedback there).
- Only WebSocket upgrades whose `Origin` is `moz-extension://…` (or
  `chrome-extension://…`) are accepted, and the listener binds to `127.0.0.1`
  only.

## Target tab

The extension decides which YouTube tab to control:

1. A tab you **pinned** from the extension popup ("Pin this tab"), else
2. the **most recently used** YouTube tab (focused, or where you last pressed a
   shortcut / started playback).

## Setup

1. In Firefox, open the extension's **Settings** → *Global hotkeys (optional
   local app)* → enable **"Enable connection to local app"** and make sure the
   **Port** matches the app (default `8423`).
2. Run `PlayControlAgent.exe`. A tray icon appears.
3. The extension connects automatically. The popup and settings page show
   "local app connected".

## Tray menu

- **Enable / Disable hotkeys** — toggle global hotkeys without quitting (same
  as pressing the master toggle hotkey, default `Ctrl+Alt+P`).
- **Settings…** — opens the settings window (double-clicking the tray icon does
  the same). Three tabs:
  - **General** — WebSocket port, master toggle hotkey (click the box and press
    a combo to record it), start-with-Windows.
  - **Overlay (OSD)** — show/hide the overlay, corner, font size, duration,
    color picker, hide-while-browser-focused, and a **Preview** button.
  - **Hotkey status** — live list of every global hotkey synced from the
    extension and whether it is **active**, **off** (master toggle disabled),
    or **failed** (e.g. already in use by another app), plus connection state.
  Changes apply immediately on **Save** (the server restarts if you change the
  port).
- **Edit config file… / Reload config file** — open/re-read `config.json`
  directly, for power users who prefer editing JSON.
- **Start with Windows** — toggles an entry under
  `HKCU\…\CurrentVersion\Run`.
- **Exit**.

Only one instance runs at a time; launching a second copy just shows a notice
and exits.

## Configuration (`config.json`)

See `config.example.json`. Keys:

| Field | Meaning |
|-------|---------|
| `Port` | WebSocket port; must match the extension setting. |
| `ShowOverlay` | Show the on-screen feedback overlay. |
| `OverlayFontSize` | Overlay font size (px). |
| `OverlayColor` | Overlay text color (hex). |
| `OverlayCorner` | `top-right` \| `top-left` \| `bottom-right` \| `bottom-left`. |
| `OverlayDurationMs` | How long the overlay stays visible. |
| `ToggleHotkey` | Master enable/disable combo (default `Ctrl+Alt+P`). |
| `SuppressOverlayWhenBrowserFocused` | Hide the corner overlay while the browser is focused (default `true`). |

> **Hotkeys themselves are no longer configured here** — set them in the
> extension's Settings page. The app registers whatever the extension syncs.
> (Old `config.json` files with a `Hotkeys` block still parse; that block is
> just ignored.)

**Combo syntax** (used by the extension, understood by the app): modifiers
`Ctrl` / `Alt` / `Shift` / `Win` joined with `+`, plus one key. Key names use
`KeyboardEvent.code` values — `Home`, `End`, `PageUp`, `PageDown`, `Insert`,
`Delete`, `ArrowLeft`/`ArrowRight`/`ArrowUp`/`ArrowDown`, `KeyA`–`KeyZ`,
`Digit0`–`Digit9`, `Numpad0`–`Numpad9`, `F1`–`F24` — and common aliases
(`Left`, `PgUp`, single letters/digits) are also accepted.

Example (just the master toggle and overlay style):

```json
{
  "Port": 8423,
  "ToggleHotkey": "Ctrl+Alt+P",
  "OverlayCorner": "top-right"
}
```

## Building from source

Requires the **.NET 8 SDK**.

```powershell
cd local-app
.\build.ps1
# -> publish\PlayControlAgent.exe  (self-contained, ~70 MB, no runtime needed)
```

Or a smaller, framework-dependent build (requires the
**.NET 8 Desktop Runtime** on the target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-fd
```

## Notes / limitations

- **Windows only.** Global hotkey registration uses the Win32
  `RegisterHotKey` API.
- Some **exclusive-fullscreen games** capture all input and may swallow global
  hotkeys.
- If a hotkey "could not be registered", another app already owns that
  combination — change it in the extension's Settings, or press the master
  toggle to release the keys.
- Because bare keys (`Home`/`End`/`Delete`/`PageUp`/`PageDown`) are registered
  globally, they are intercepted everywhere while hotkeys are enabled. Press
  the master toggle (`Ctrl+Alt+P`) or *Disable hotkeys* to use them normally
  in other apps.
- The exe is unsigned; SmartScreen may warn on first run ("More info" → "Run
  anyway").
