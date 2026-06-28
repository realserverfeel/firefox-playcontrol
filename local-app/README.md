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
- When a hotkey fires, the app sends `{type:"command", id, action}` to the
  extension; the extension runs the action on the target tab and replies with
  `{type:"result", id, action, ok, info}` so the app can show accurate overlay
  feedback.
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

- **Enable / Disable hotkeys** — toggle global hotkeys without quitting.
- **Edit configuration…** — opens `config.json` (next to the exe) in your
  editor. Change hotkeys, port, overlay style.
- **Reload configuration** — re-reads `config.json` and re-registers hotkeys
  (also restarts the server if you changed the port).
- **Show status** — connection state + current hotkey map.
- **Start with Windows** — toggles an entry under
  `HKCU\…\CurrentVersion\Run`.
- **Exit**.

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
| `Hotkeys` | Map of `"combo"` → `action`. |

**Combo syntax:** modifiers `Ctrl` / `Alt` / `Shift` / `Win` joined with `+`,
plus one key. Key names include arrows (`Left`/`Right`/`Up`/`Down`), `Home`,
`End`, `PageUp`/`PgUp`, `PageDown`/`PgDn`, `Insert`, `Delete`, `Space`, `Enter`,
`F1`–`F24`, `Numpad0`–`Numpad9`, digits `0`–`9`, and letters `A`–`Z`.

**Actions** (must match the extension):
`togglePlay`, `seekBack`, `seekForward`, `markBookmark`, `prevBookmark`,
`nextBookmark`, `playSegment`, `copyUrl`, `clearBookmarks`.

Example:

```json
{
  "Port": 8423,
  "Hotkeys": {
    "Ctrl+Alt+Home": "togglePlay",
    "Ctrl+Alt+Right": "seekForward",
    "Ctrl+Alt+Left": "seekBack"
  }
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
  combination — pick a different one in `config.json`.
- The exe is unsigned; SmartScreen may warn on first run ("More info" → "Run
  anyway").
