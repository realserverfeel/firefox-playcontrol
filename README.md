# firefox-playcontrol

A keyboard-driven Firefox extension for controlling YouTube playback, with a
bookmark system and a persistent, exportable bookmark manager.

Designed to be **resilient to YouTube UI changes**: it only operates on the
standard HTML `<video>` element and injects a single overlay element for
on-screen feedback. It never modifies YouTube's own player controls, progress
bar, or layout.

## Features

- **Playback control** (default shortcuts):
  - `Home` — Play / Pause
  - `Insert` — Seek backward (default 5s, configurable)
  - `Delete` — Seek forward (default 5s, configurable)
- **Bookmarks** (stored per video by videoId):
  - `End` — Add bookmark at current time
  - `Page Up` — Jump to previous bookmark
  - `Page Down` — Jump to next bookmark
  - *Play current segment* (jump to previous bookmark, play until the next
    bookmark, then pause) — unassigned by default, set it in Settings
  - *Copy URL at current time* (no bookmark created) — unassigned by default
  - *Clear all bookmarks for current video* — unassigned by default; optional
    two-press confirmation (toggle in Settings)
- **PotPlayer-style OSD**: blue text in the top-right of the video shows each
  action. Font size, color, and duration are configurable.
- **Bookmark manager** (full page): list of all videos with thumbnails, search
  and sort, per-video bookmark editing (notes, jump, copy timestamped URL,
  delete), and timeline preview.
- **Data**: stored in `browser.storage.local` with the `unlimitedStorage`
  permission (no practical size cap). JSON export / import (merge or replace).
- **Native controls**: optional setting to wake YouTube's native player
  controls / seek bar when a shortcut is used (off by default to keep the
  minimal footprint).
- All shortcuts are remappable in **Settings**, and each action can be bound to
  multiple keys (a key can only belong to one action).

## Project structure

```
manifest.json          Manifest V2 (Firefox)
src/defaults.js        Shared defaults/constants (window.PC_DEFAULTS)
src/storage.js         Storage CRUD layer (window.PC_STORE)
src/shortcuts.js       Keyboard combo helpers (window.PC_KEYS)
src/content.js         In-page control + OSD overlay
src/osd.css            OSD overlay styling
src/background.js      Opens manager/options pages
popup/                 Toolbar popup (minimal status + entry points)
options/               Settings page (shortcuts, seek step, OSD, data)
manager/               Bookmark manager page
icons/                 Toolbar/extension icons
```

## Install for development (temporary)

1. Open `about:debugging#/runtime/this-firefox` in Firefox.
2. Click **Load Temporary Add-on…**
3. Select `manifest.json` in this repo.
4. Open a YouTube video and use the shortcuts above.

Or, with [`web-ext`](https://github.com/mozilla/web-ext):

```bash
npx web-ext run            # launches Firefox with the extension loaded
npx web-ext lint           # validates the extension
npx web-ext build          # produces a distributable .zip in web-ext-artifacts/
```

## Roadmap (Phase 2)

Global hotkeys that work outside the browser (control a chosen YouTube tab from
anywhere in Windows) via a small local app that registers system hotkeys and
relays commands to the extension over a local WebSocket. Not yet implemented.
