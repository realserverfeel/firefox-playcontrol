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

  async function resolveTargetTabId() {
    if (await tabExistsAndYouTube(pinnedTabId)) return pinnedTabId;
    if (pinnedTabId != null) pinnedTabId = null; // pinned tab gone
    if (await tabExistsAndYouTube(lastActiveYouTubeTabId)) {
      return lastActiveYouTubeTabId;
    }
    lastActiveYouTubeTabId = null;
    // Fall back to the most recently accessed YouTube tab.
    try {
      const tabs = await api.tabs.query({ url: "*://*.youtube.com/*" });
      if (tabs && tabs.length) {
        tabs.sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0));
        return tabs[0].id;
      }
    } catch (e) {
      /* ignore */
    }
    return null;
  }

  // Run an action on the resolved target tab. Returns { ok, info } so the
  // companion app can render accurate feedback in its overlay.
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
      return { ok: true, info: info };
    } catch (e) {
      // Content script not present (e.g. tab still loading).
      return { ok: false, info: "tab-unreachable" };
    }
  }

  // ---- bridge lifecycle --------------------------------------------------

  if (BRIDGE) {
    BRIDGE.setCommandHandler(routeCommand);
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
    }
  });

  // ---- tab activity ------------------------------------------------------

  api.tabs.onActivated.addListener(async ({ tabId }) => {
    if (await tabExistsAndYouTube(tabId)) lastActiveYouTubeTabId = tabId;
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
      if (sender.tab && isYouTubeUrl(sender.tab.url)) {
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
