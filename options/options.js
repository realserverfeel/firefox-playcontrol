(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;
  const STORE = window.PC_STORE;
  const KEYS = window.PC_KEYS;
  const D = window.PC_DEFAULTS;

  let settings = null;
  let recordingAction = null;

  const els = {
    shortcuts: document.getElementById("shortcuts"),
    seekSeconds: document.getElementById("seekSeconds"),
    activateNativeControls: document.getElementById("activateNativeControls"),
    confirmClearBookmarks: document.getElementById("confirmClearBookmarks"),
    bookmarkProximity: document.getElementById("bookmarkProximity"),
    osdEnabled: document.getElementById("osdEnabled"),
    osdFontSize: document.getElementById("osdFontSize"),
    osdColor: document.getElementById("osdColor"),
    osdDuration: document.getElementById("osdDuration"),
    ghEnabled: document.getElementById("ghEnabled"),
    ghPort: document.getElementById("ghPort"),
    ghStatus: document.getElementById("ghStatus"),
    toast: document.getElementById("toast"),
  };

  function toast(msg) {
    els.toast.textContent = msg;
    els.toast.classList.add("show");
    setTimeout(() => els.toast.classList.remove("show"), 1600);
  }

  async function save() {
    await STORE.setSettings(settings);
  }

  function getCombos(action) {
    const v = settings.shortcuts[action];
    return Array.isArray(v) ? v : v ? [v] : [];
  }

  function renderShortcuts() {
    els.shortcuts.innerHTML = "";
    for (const action of Object.keys(D.DEFAULT_SHORTCUTS)) {
      const row = document.createElement("div");
      row.className = "sc-row";

      const label = document.createElement("div");
      label.className = "sc-label";
      label.textContent = D.ACTION_LABELS[action] || action;

      const keys = document.createElement("div");
      keys.className = "sc-keys";

      const combos = getCombos(action);
      if (combos.length === 0) {
        const none = document.createElement("span");
        none.className = "sc-none";
        none.textContent = "(no keys)";
        keys.appendChild(none);
      }
      for (const combo of combos) {
        const chip = document.createElement("span");
        chip.className = "sc-chip";

        const txt = document.createElement("span");
        txt.textContent = KEYS.prettyCombo(combo);
        chip.appendChild(txt);

        const rm = document.createElement("button");
        rm.className = "sc-remove";
        rm.type = "button";
        rm.textContent = "\u00D7";
        rm.title = "Remove";
        rm.addEventListener("click", () => removeCombo(action, combo));
        chip.appendChild(rm);

        keys.appendChild(chip);
      }

      const add = document.createElement("button");
      add.className = "sc-add";
      add.type = "button";
      add.dataset.action = action;
      if (recordingAction === action) {
        add.textContent = "Press keys\u2026 (Esc to cancel)";
        add.classList.add("recording");
      } else {
        add.textContent = "+ Add key";
      }
      add.addEventListener("click", () => startRecording(action));
      keys.appendChild(add);

      row.appendChild(label);
      row.appendChild(keys);
      els.shortcuts.appendChild(row);
    }
  }

  function removeCombo(action, combo) {
    settings.shortcuts[action] = getCombos(action).filter((c) => c !== combo);
    renderShortcuts();
    save().then(() => toast("Saved"));
  }

  function startRecording(action) {
    recordingAction = recordingAction === action ? null : action;
    renderShortcuts();
  }

  function onKeyDownCapture(e) {
    if (!recordingAction) return;
    e.preventDefault();
    e.stopPropagation();

    if (e.code === "Escape") {
      recordingAction = null;
      renderShortcuts();
      return;
    }
    const combo = KEYS.comboFromEvent(e);
    if (!combo) return; // lone modifier, keep waiting

    const action = recordingAction;

    // No duplicates: remove this combo from every action (including this one),
    // then add it to the target action.
    for (const a of Object.keys(settings.shortcuts)) {
      settings.shortcuts[a] = getCombos(a).filter((c) => c !== combo);
    }
    settings.shortcuts[action].push(combo);

    recordingAction = null;
    renderShortcuts();
    save().then(() => toast("Shortcut saved"));
  }

  function bindFields() {
    els.seekSeconds.value = settings.seekSeconds;
    els.activateNativeControls.checked = settings.activateNativeControls;
    els.confirmClearBookmarks.checked = settings.confirmClearBookmarks;
    els.bookmarkProximity.value = settings.bookmarkProximity;
    els.osdEnabled.checked = settings.osd.enabled;
    els.osdFontSize.value = settings.osd.fontSize;
    els.osdColor.value = settings.osd.color;
    els.osdDuration.value = settings.osd.durationMs;
    const gh = settings.globalHotkeys || { enabled: false, port: 8423 };
    els.ghEnabled.checked = !!gh.enabled;
    els.ghPort.value = gh.port || 8423;

    els.seekSeconds.addEventListener("change", () => {
      settings.seekSeconds = Math.max(1, parseInt(els.seekSeconds.value, 10) || 5);
      els.seekSeconds.value = settings.seekSeconds;
      save().then(() => toast("Saved"));
    });
    els.activateNativeControls.addEventListener("change", () => {
      settings.activateNativeControls = els.activateNativeControls.checked;
      save().then(() => toast("Saved"));
    });
    els.confirmClearBookmarks.addEventListener("change", () => {
      settings.confirmClearBookmarks = els.confirmClearBookmarks.checked;
      save().then(() => toast("Saved"));
    });
    els.bookmarkProximity.addEventListener("change", () => {
      let v = parseFloat(els.bookmarkProximity.value);
      if (isNaN(v) || v < 0) v = 0.8;
      settings.bookmarkProximity = v;
      els.bookmarkProximity.value = v;
      save().then(() => toast("Saved"));
    });
    els.osdEnabled.addEventListener("change", () => {
      settings.osd.enabled = els.osdEnabled.checked;
      save().then(() => toast("Saved"));
    });
    els.osdFontSize.addEventListener("change", () => {
      settings.osd.fontSize = Math.max(10, parseInt(els.osdFontSize.value, 10) || 24);
      els.osdFontSize.value = settings.osd.fontSize;
      save().then(() => toast("Saved"));
    });
    els.osdColor.addEventListener("change", () => {
      settings.osd.color = els.osdColor.value;
      save().then(() => toast("Saved"));
    });
    els.osdDuration.addEventListener("change", () => {
      settings.osd.durationMs = Math.max(300, parseInt(els.osdDuration.value, 10) || 1500);
      els.osdDuration.value = settings.osd.durationMs;
      save().then(() => toast("Saved"));
    });
    els.ghEnabled.addEventListener("change", () => {
      if (!settings.globalHotkeys) settings.globalHotkeys = { enabled: false, port: 8423 };
      settings.globalHotkeys.enabled = els.ghEnabled.checked;
      save().then(() => toast("Saved"));
    });
    els.ghPort.addEventListener("change", () => {
      if (!settings.globalHotkeys) settings.globalHotkeys = { enabled: false, port: 8423 };
      let p = parseInt(els.ghPort.value, 10);
      if (isNaN(p) || p < 1 || p > 65535) p = 8423;
      settings.globalHotkeys.port = p;
      els.ghPort.value = p;
      save().then(() => toast("Saved"));
    });
  }

  function pollGhStatus() {
    api.runtime.sendMessage({ type: "PC_GET_BRIDGE_STATE" }).then((res) => {
      const st = res && res.bridge;
      if (!st || !st.enabled) {
        els.ghStatus.textContent = "disabled";
        els.ghStatus.className = "gh-status";
      } else if (st.connected) {
        els.ghStatus.textContent = "\u25CF connected to local app";
        els.ghStatus.className = "gh-status ok";
      } else {
        els.ghStatus.textContent = "\u25CB enabled, waiting for local app\u2026";
        els.ghStatus.className = "gh-status wait";
      }
    }).catch(() => {});
  }

  // ---- data management ---------------------------------------------------

  function download(filename, text) {
    const blob = new Blob([text], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  document.getElementById("export").addEventListener("click", async () => {
    const data = await STORE.exportData();
    const stamp = new Date().toISOString().slice(0, 10);
    download(`playcontrol-${stamp}.json`, JSON.stringify(data, null, 2));
    toast("Exported");
  });

  const importFile = document.getElementById("importFile");
  document.getElementById("importBtn").addEventListener("click", () => importFile.click());
  importFile.addEventListener("change", async () => {
    const file = importFile.files[0];
    if (!file) return;
    try {
      const text = await file.text();
      const data = JSON.parse(text);
      const replace = confirm(
        "Import bookmarks.\n\nOK = Merge with existing data\nCancel = Replace all existing data"
      );
      await STORE.importData(data, replace ? "merge" : "replace");
      settings = await STORE.getSettings();
      renderShortcuts();
      bindFields();
      toast("Imported");
    } catch (e) {
      alert("Import failed: " + e.message);
    } finally {
      importFile.value = "";
    }
  });

  document.getElementById("openManager").addEventListener("click", () => {
    api.runtime.sendMessage({ type: "PC_OPEN_MANAGER" });
  });

  document.getElementById("clearAll").addEventListener("click", async () => {
    if (!confirm("Delete ALL videos and bookmarks? This cannot be undone.")) return;
    await STORE.clearAll();
    toast("All bookmarks cleared");
  });

  document.getElementById("osdPreview").addEventListener("click", async () => {
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    const tab = tabs[0];
    if (!tab || !/youtube\.com/.test(tab.url || "")) {
      toast("Open a YouTube tab first");
      return;
    }
    api.tabs.sendMessage(tab.id, { type: "PC_RUN_ACTION", action: "togglePlay" })
      .catch(() => toast("Reload the YouTube tab"));
    toast("Sent test to YouTube tab");
  });

  // ---- init --------------------------------------------------------------

  document.addEventListener("keydown", onKeyDownCapture, true);

  STORE.getSettings().then((s) => {
    settings = s;
    renderShortcuts();
    bindFields();
    pollGhStatus();
    setInterval(pollGhStatus, 1500);
  });
})();
