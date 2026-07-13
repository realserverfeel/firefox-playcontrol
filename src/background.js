// Background script: opens extension pages, tracks which YouTube tab global
// hotkeys should control, and drives the optional local-app bridge.
//
// Everything here is additive: with the bridge disabled or the companion app
// absent, the extension's in-page shortcuts keep working exactly as before.

(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;
  const D = window.PC_DEFAULTS;
  const STORE = window.PC_STORE;
  const BRIDGE = window.PC_BRIDGE;

  // ---- target-tab tracking ----------------------------------------------

  // The tab global hotkeys act on. Resolution order: a manually pinned tab,
  // then the most recently active/used YouTube tab. All in-memory: tab ids are
  // ephemeral, so nothing is persisted.
  let pinnedTabId = null;
  let lastActiveYouTubeTabId = null;

  function isYouTubeUrl(u) {
    return typeof u === "string" && /^https?:\/\/([^/]*\.)?youtube\.com\//.test(u);
  }

  async function tabExistsAndYouTube(tabId) {
    if (tabId == null) return false;
    try {
      const tab = await api.tabs.get(tabId);
      return !!tab && isYouTubeUrl(tab.url);
    } catch (e) {
      return false;
    }
  }

  // Whether the tab currently has a playable <video>. A YouTube tab that has
  // navigated to the home page, a channel, or search results has none, so it
  // must not be treated as a control target even though its URL is youtube.com.
  async function tabHasVideo(tabId) {
    if (tabId == null) return false;
    try {
      const resp = await api.tabs.sendMessage(tabId, { type: "PC_GET_STATE" });
      return !!(resp && resp.hasVideo);
    } catch (e) {
      return false;
    }
  }

  async function resolveTargetTabId() {
    // A manual pin is an explicit override: honor it while the tab still exists
    // and has a video.
    if (await tabHasVideo(pinnedTabId)) return pinnedTabId;
    if (pinnedTabId != null && !(await tabExistsAndYouTube(pinnedTabId))) {
      pinnedTabId = null; // pinned tab gone
    }
    if (await tabHasVideo(lastActiveYouTubeTabId)) {
      return lastActiveYouTubeTabId;
    }
    lastActiveYouTubeTabId = null;
    // Fall back to the most recently accessed YouTube tab that actually has a
    // video, so navigating another tab to the home page never steals control.
    try {
      const tabs = await api.tabs.query({ url: "*://*.youtube.com/*" });
      if (tabs && tabs.length) {
        tabs.sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0));
        for (const t of tabs) {
          if (await tabHasVideo(t.id)) {
            lastActiveYouTubeTabId = t.id;
            return t.id;
          }
        }
      }
    } catch (e) {
      /* ignore */
    }
    return null;
  }

  // Whether the target tab is the active tab of the currently focused browser
  // window. The companion app uses this to decide if its overlay would be
  // redundant: only when the controlled tab is actually visible does the
  // in-page OSD show, so the app suppresses its overlay only in that case.
  async function isTargetTabVisible(tabId) {
    try {
      const tabs = await api.tabs.query({ active: true, lastFocusedWindow: true });
      return !!(tabs && tabs.length > 0 && tabs[0].id === tabId);
    } catch (e) {
      return false;
    }
  }

  // Run an action on the resolved target tab. Returns { ok, info, visible } so
  // the companion app can render accurate feedback in its overlay.
  async function routeCommand(action) {
    const tabId = await resolveTargetTabId();
    if (tabId == null) return { ok: false, info: "no-youtube-tab" };
    try {
      await api.tabs.sendMessage(tabId, { type: "PC_RUN_ACTION", action: action });
      let info = null;
      try {
        const state = await api.tabs.sendMessage(tabId, { type: "PC_GET_STATE" });
        if (state) {
          info = {
            title: state.title,
            currentTime: state.currentTime,
            paused: state.paused,
            bookmarkCount: state.bookmarkCount,
          };
        }
      } catch (e) {
        /* state is best-effort */
      }
      const visible = await isTargetTabVisible(tabId);
      return { ok: true, info: info, visible: visible };
    } catch (e) {
      // Content script not present (e.g. tab still loading).
      return { ok: false, info: "tab-unreachable" };
    }
  }

  // ---- bridge lifecycle --------------------------------------------------

  // The local app registers the *same* shortcuts the user configured in the
  // extension as system-wide global hotkeys. We push the current map on every
  // (re)connect and whenever settings change.
  async function getShortcutMap() {
    try {
      const s = await STORE.getSettings();
      const sc = (s && s.shortcuts) || {};
      return window.PC_KEYS ? window.PC_KEYS.normalizeShortcuts(sc) : sc;
    } catch (e) {
      return {};
    }
  }

  if (BRIDGE) {
    BRIDGE.setCommandHandler(routeCommand);
    BRIDGE.setShortcutsProvider(getShortcutMap);
  }

  async function applyBridgeSettings() {
    if (!BRIDGE) return;
    const s = await STORE.getSettings();
    const gh = s.globalHotkeys || {};
    BRIDGE.configure(!!gh.enabled, Number(gh.port) || 8423);
  }

  applyBridgeSettings();

  api.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && changes[D.STORAGE_KEYS.settings]) {
      applyBridgeSettings();
      // Re-push shortcuts so the app re-registers global hotkeys live.
      if (BRIDGE) BRIDGE.pushShortcuts();
    }
  });

  // ---- tab activity ------------------------------------------------------

  api.tabs.onActivated.addListener(async ({ tabId }) => {
    if (await tabHasVideo(tabId)) lastActiveYouTubeTabId = tabId;
  });

  api.tabs.onRemoved.addListener((tabId) => {
    if (tabId === pinnedTabId) pinnedTabId = null;
    if (tabId === lastActiveYouTubeTabId) lastActiveYouTubeTabId = null;
  });

  // ---- messaging ---------------------------------------------------------

  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || !msg.type) return;

    if (msg.type === "PC_OPEN_MANAGER") {
      api.tabs.create({ url: api.runtime.getURL("manager/manager.html") });
      sendResponse({ ok: true });
      return false;
    }
    if (msg.type === "PC_OPEN_OPTIONS") {
      if (api.runtime.openOptionsPage) api.runtime.openOptionsPage();
      sendResponse({ ok: true });
      return false;
    }
    // A YouTube tab reports user activity (key used / playback). Mark it as the
    // most recent control target.
    if (msg.type === "PC_ACTIVITY") {
      if (sender.tab && msg.hasVideo && isYouTubeUrl(sender.tab.url)) {
        lastActiveYouTubeTabId = sender.tab.id;
      }
      return false;
    }
    if (msg.type === "PC_GET_BRIDGE_STATE") {
      const st = BRIDGE ? BRIDGE.status() : { enabled: false, connected: false, port: 0 };
      sendResponse({
        bridge: st,
        pinnedTabId: pinnedTabId,
      });
      return false;
    }
    if (msg.type === "PC_SET_PIN") {
      pinnedTabId = typeof msg.tabId === "number" ? msg.tabId : null;
      sendResponse({ ok: true, pinnedTabId: pinnedTabId });
      return false;
    }
    if (msg.type === "PC_CLEAR_PIN") {
      pinnedTabId = null;
      sendResponse({ ok: true, pinnedTabId: null });
      return false;
    }
  });
})();
