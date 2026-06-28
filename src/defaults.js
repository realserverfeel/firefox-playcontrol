// Shared defaults and constants for PlayControl.
// Loaded as a plain script (no modules) so it works in content scripts,
// background, and extension pages alike. Exposes `window.PC_DEFAULTS`.

(function () {
  "use strict";

  const ACTIONS = {
    togglePlay: "togglePlay",
    seekBack: "seekBack",
    seekForward: "seekForward",
    markBookmark: "markBookmark",
    prevBookmark: "prevBookmark",
    nextBookmark: "nextBookmark",
    playSegment: "playSegment",
    copyUrl: "copyUrl",
    clearBookmarks: "clearBookmarks",
  };

  // Human-readable labels for the options UI.
  const ACTION_LABELS = {
    togglePlay: "Play / Pause",
    seekBack: "Seek backward",
    seekForward: "Seek forward",
    markBookmark: "Add bookmark",
    prevBookmark: "Previous bookmark",
    nextBookmark: "Next bookmark",
    playSegment: "Play current segment (prev bookmark \u2192 next bookmark)",
    copyUrl: "Copy URL at current time",
    clearBookmarks: "Clear all bookmarks for current video",
  };

  // Default shortcuts use KeyboardEvent.code values (physical keys), so they
  // are layout-independent. Each action holds an array of combos; an empty
  // array means "unassigned". A single action may have multiple bindings.
  const DEFAULT_SHORTCUTS = {
    togglePlay: ["Home"],
    seekBack: ["Insert"],
    seekForward: ["Delete"],
    markBookmark: ["End"],
    prevBookmark: ["PageUp"],
    nextBookmark: ["PageDown"],
    playSegment: [],
    copyUrl: [],
    clearBookmarks: [],
  };

  const DEFAULT_SETTINGS = {
    seekSeconds: 5,
    shortcuts: { ...DEFAULT_SHORTCUTS },
    // Require pressing the clear-bookmarks shortcut twice within a few seconds.
    confirmClearBookmarks: true,
    // Proximity window (seconds). A bookmark passed less than this long ago is
    // skipped by "previous bookmark" (jumps to the one before it instead).
    bookmarkProximity: 0.8,
    // When true, control actions wake YouTube's native player controls / seek
    // bar (best-effort synthetic mousemove on the player). Off keeps the
    // minimal, no-touch footprint.
    activateNativeControls: false,
    // Optional bridge to a local companion app that registers system-wide
    // global hotkeys and forwards them over a loopback WebSocket. The
    // extension works fully without it; when enabled it merely *tries* to
    // connect and silently retries. See README "Global hotkeys".
    globalHotkeys: {
      enabled: false,
      port: 8423,
    },
    osd: {
      enabled: true,
      fontSize: 24,
      color: "#3b9dff",
      durationMs: 1500,
    },
  };

  const STORAGE_KEYS = {
    settings: "settings",
    videos: "videos",
  };

  window.PC_DEFAULTS = {
    ACTIONS,
    ACTION_LABELS,
    DEFAULT_SHORTCUTS,
    DEFAULT_SETTINGS,
    STORAGE_KEYS,
    SCHEMA_VERSION: 1,
  };
})();
