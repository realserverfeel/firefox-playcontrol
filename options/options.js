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

  function renderShortcuts() {
    els.shortcuts.innerHTML = "";
    for (const action of Object.keys(D.DEFAULT_SHORTCUTS)) {
      const row = document.createElement("div");
      row.className = "sc-row";

      const label = document.createElement("div");
      label.className = "sc-label";
      label.textContent = D.ACTION_LABELS[action] || action;

      const key = document.createElement("div");
      key.className = "sc-key";
      const combo = settings.shortcuts[action];
      key.textContent = combo ? KEYS.prettyCombo(combo) : "(unset)";
      if (!combo) key.classList.add("unset");
      key.dataset.action = action;

      key.addEventListener("click", () => startRecording(action, key));

      row.appendChild(label);
      row.appendChild(key);
      els.shortcuts.appendChild(row);
    }
  }

  function startRecording(action, keyEl) {
    // Stop any other recording.
    document.querySelectorAll(".sc-key.recording").forEach((el) => {
      el.classList.remove("recording");
    });
    recordingAction = action;
    keyEl.classList.add("recording");
    keyEl.textContent = "Press keys\u2026";
  }

  function onKeyDownCapture(e) {
    if (!recordingAction) return;
    e.preventDefault();
    e.stopPropagation();

    const keyEl = document.querySelector(`.sc-key[data-action="${recordingAction}"]`);

    if (e.code === "Escape") {
      settings.shortcuts[recordingAction] = "";
      finishRecording(keyEl);
      return;
    }
    const combo = KEYS.comboFromEvent(e);
    if (!combo) return; // lone modifier, keep waiting

    // Prevent duplicate assignment: clear the same combo from other actions.
    for (const a of Object.keys(settings.shortcuts)) {
      if (a !== recordingAction && settings.shortcuts[a] === combo) {
        settings.shortcuts[a] = "";
      }
    }
    settings.shortcuts[recordingAction] = combo;
    finishRecording(keyEl);
  }

  function finishRecording() {
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
  });
})();
