(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;

  const titleEl = document.getElementById("title");
  const metaEl = document.getElementById("meta");
  const hintEl = document.getElementById("hint");

  function fmtTime(sec) {
    sec = Math.max(0, Math.floor(sec || 0));
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}:${String(s).padStart(2, "0")}`;
  }

  async function queryActiveTab() {
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    const tab = tabs[0];
    if (!tab || !/youtube\.com/.test(tab.url || "")) {
      titleEl.textContent = "No YouTube tab active";
      metaEl.textContent = "Open a YouTube video to use shortcuts.";
      return;
    }
    api.tabs.sendMessage(tab.id, { type: "PC_GET_STATE" }).then((state) => {
      if (!state || !state.hasVideo) {
        titleEl.textContent = "No video detected";
        metaEl.textContent = "";
        return;
      }
      titleEl.textContent = state.title || "(untitled)";
      const status = state.paused ? "\u2759\u2759 Paused" : "\u25B6 Playing";
      metaEl.textContent = `${status} \u00B7 ${fmtTime(state.currentTime)} \u00B7 \uD83D\uDCCC ${state.bookmarkCount} bookmark(s)`;
    }).catch(() => {
      titleEl.textContent = "Content script not loaded";
      metaEl.textContent = "Reload the YouTube tab.";
    });
  }

  document.getElementById("open-manager").addEventListener("click", () => {
    api.runtime.sendMessage({ type: "PC_OPEN_MANAGER" });
    window.close();
  });

  document.getElementById("open-options").addEventListener("click", () => {
    if (api.runtime.openOptionsPage) api.runtime.openOptionsPage();
    else api.runtime.sendMessage({ type: "PC_OPEN_OPTIONS" });
    window.close();
  });

  window.PC_STORE.getSettings().then((s) => {
    const sc = s.shortcuts;
    hintEl.textContent =
      `Shortcuts work while the YouTube tab is focused. ` +
      `Play/Pause, seek \u00B1${s.seekSeconds}s, and bookmarks are configurable in Settings.`;
  });

  queryActiveTab();
})();
