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
  };

  // Default shortcuts use KeyboardEvent.code values (physical keys), so they
  // are layout-independent. Empty string means "unassigned".
  const DEFAULT_SHORTCUTS = {
    togglePlay: "Home",
    seekBack: "Insert",
    seekForward: "Delete",
    markBookmark: "End",
    prevBookmark: "PageUp",
    nextBookmark: "PageDown",
    playSegment: "",
    copyUrl: "",
  };

  const DEFAULT_SETTINGS = {
    seekSeconds: 5,
    shortcuts: { ...DEFAULT_SHORTCUTS },
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
