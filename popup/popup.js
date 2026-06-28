(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;

  const titleEl = document.getElementById("title");
  const metaEl = document.getElementById("meta");
  const hintEl = document.getElementById("hint");
  const bridgeEl = document.getElementById("bridge");
  const bridgeStatusEl = document.getElementById("bridge-status");
  const pinRowEl = document.getElementById("pin-row");
  const pinStateEl = document.getElementById("pin-state");
  const pinBtn = document.getElementById("pin-btn");

  let currentTabId = null;
  let currentTabIsYouTube = false;

  function fmtTime(sec) {
    sec = Math.max(0, Math.floor(sec || 0));
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}:${String(s).padStart(2, "0")}`;
  }

  async function queryActiveTab() {
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    const tab = tabs[0];
    currentTabId = tab ? tab.id : null;
    currentTabIsYouTube = !!(tab && /youtube\.com/.test(tab.url || ""));
    refreshBridge();
    if (!currentTabIsYouTube) {
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

  function refreshBridge() {
    api.runtime.sendMessage({ type: "PC_GET_BRIDGE_STATE" }).then((res) => {
      if (!res || !res.bridge || !res.bridge.enabled) {
        bridgeEl.classList.add("hidden");
        return;
      }
      bridgeEl.classList.remove("hidden");
      const st = res.bridge;
      if (st.connected) {
        bridgeStatusEl.textContent = "\u25CF local app connected";
        bridgeStatusEl.className = "bridge-status ok";
      } else {
        bridgeStatusEl.textContent = "\u25CB waiting for local app\u2026";
        bridgeStatusEl.className = "bridge-status wait";
      }

      const pinned = res.pinnedTabId;
      if (pinned != null && pinned === currentTabId) {
        pinStateEl.textContent = "This tab is the control target";
        pinBtn.textContent = "Unpin";
        pinBtn.dataset.mode = "unpin";
        pinRowEl.classList.remove("hidden");
      } else if (pinned != null) {
        pinStateEl.textContent = "Another tab is pinned";
        pinBtn.textContent = currentTabIsYouTube ? "Pin this tab" : "Clear pin";
        pinBtn.dataset.mode = currentTabIsYouTube ? "pin" : "unpin";
        pinRowEl.classList.remove("hidden");
      } else if (currentTabIsYouTube) {
        pinStateEl.textContent = "Targets last-used YouTube tab";
        pinBtn.textContent = "Pin this tab";
        pinBtn.dataset.mode = "pin";
        pinRowEl.classList.remove("hidden");
      } else {
        pinRowEl.classList.add("hidden");
      }
    }).catch(() => {
      bridgeEl.classList.add("hidden");
    });
  }

  pinBtn.addEventListener("click", () => {
    const mode = pinBtn.dataset.mode;
    if (mode === "pin" && currentTabId != null) {
      api.runtime.sendMessage({ type: "PC_SET_PIN", tabId: currentTabId }).then(refreshBridge);
    } else {
      api.runtime.sendMessage({ type: "PC_CLEAR_PIN" }).then(refreshBridge);
    }
  });

  window.PC_STORE.getSettings().then((s) => {
    const sc = s.shortcuts;
    hintEl.textContent =
      `Shortcuts work while the YouTube tab is focused. ` +
      `Play/Pause, seek \u00B1${s.seekSeconds}s, and bookmarks are configurable in Settings.`;
  });

  queryActiveTab();
})();
