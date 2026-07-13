(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;
  const STORE = window.PC_STORE;

  const listView = document.getElementById("list-view");
  const detailView = document.getElementById("detail-view");
  const videosEl = document.getElementById("videos");
  const emptyEl = document.getElementById("empty");
  const searchEl = document.getElementById("search");
  const sortEl = document.getElementById("sort");
  const toastEl = document.getElementById("toast");

  let currentVideoId = null;

  function toast(msg) {
    toastEl.textContent = msg;
    toastEl.classList.add("show");
    setTimeout(() => toastEl.classList.remove("show"), 1500);
  }

  function fmtTime(sec) {
    sec = Math.max(0, Math.floor(sec || 0));
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    const s = sec % 60;
    const pad = (n) => String(n).padStart(2, "0");
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
  }

  function watchUrl(videoId, time) {
    let u = `https://www.youtube.com/watch?v=${videoId}`;
    if (time != null) u += `&t=${Math.floor(time)}s`;
    return u;
  }

  function thumbUrl(videoId) {
    return `https://i.ytimg.com/vi/${videoId}/mqdefault.jpg`;
  }

  // ---- list view ---------------------------------------------------------

  async function renderList() {
    const videos = await STORE.getAllVideos();
    let arr = Object.values(videos);

    const q = searchEl.value.trim().toLowerCase();
    if (q) arr = arr.filter((v) => (v.title || "").toLowerCase().includes(q) || v.videoId.includes(q));

    const sort = sortEl.value;
    arr.sort((a, b) => {
      if (sort === "bookmarks") return (b.bookmarks.length) - (a.bookmarks.length);
      if (sort === "title") return (a.title || "").localeCompare(b.title || "");
      return (b.lastWatched || 0) - (a.lastWatched || 0);
    });

    videosEl.innerHTML = "";
    emptyEl.hidden = arr.length > 0;

    for (const v of arr) {
      const card = document.createElement("div");
      card.className = "video-card";

      const thumb = document.createElement("img");
      thumb.className = "thumb";
      thumb.src = thumbUrl(v.videoId);
      thumb.alt = "";
      thumb.addEventListener("error", () => { thumb.style.visibility = "hidden"; });

      const info = document.createElement("div");
      info.className = "video-info";

      const title = document.createElement("div");
      title.className = "video-title";
      title.textContent = v.title || v.videoId;

      const sub = document.createElement("div");
      sub.className = "video-sub";
      const when = v.lastWatched ? new Date(v.lastWatched).toLocaleString() : "\u2014";
      sub.textContent = `\uD83D\uDCCC ${v.bookmarks.length} bookmark(s) \u00B7 last watched ${when}`;

      const chips = document.createElement("div");
      chips.className = "chips";
      v.bookmarks.slice(0, 6).forEach((b) => {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = fmtTime(b.time) + (b.label ? ` ${b.label}` : "");
        chips.appendChild(chip);
      });
      if (v.bookmarks.length > 6) {
        const more = document.createElement("span");
        more.className = "chip";
        more.textContent = `+${v.bookmarks.length - 6} more`;
        chips.appendChild(more);
      }

      info.appendChild(title);
      info.appendChild(sub);
      info.appendChild(chips);
      card.appendChild(thumb);
      card.appendChild(info);
      card.addEventListener("click", () => openDetail(v.videoId));
      videosEl.appendChild(card);
    }
  }

  // ---- detail view -------------------------------------------------------

  async function openDetail(videoId) {
    currentVideoId = videoId;
    const v = await STORE.getVideo(videoId);
    if (!v) return;

    document.getElementById("detail-title").textContent = v.title || videoId;
    document.getElementById("open-yt").href = watchUrl(videoId);

    renderTimeline(v);
    renderBookmarkRows(v);

    listView.hidden = true;
    detailView.hidden = false;
  }

  function renderTimeline(v) {
    const tl = document.getElementById("timeline");
    tl.innerHTML = "";
    const max = Math.max(1, ...v.bookmarks.map((b) => b.time)) * 1.05;
    v.bookmarks.forEach((b) => {
      const mark = document.createElement("div");
      mark.className = "tl-mark";
      mark.style.left = `${(b.time / max) * 100}%`;
      mark.title = fmtTime(b.time) + (b.label ? ` \u2013 ${b.label}` : "");
      tl.appendChild(mark);
    });
  }

  function renderBookmarkRows(v) {
    const body = document.getElementById("bm-body");
    body.innerHTML = "";
    v.bookmarks.forEach((b, i) => {
      const tr = document.createElement("tr");

      const tdNum = document.createElement("td");
      tdNum.textContent = i + 1;

      const tdTime = document.createElement("td");
      const link = document.createElement("a");
      link.className = "time-link";
      link.textContent = fmtTime(b.time);
      link.href = watchUrl(v.videoId, b.time);
      link.target = "_blank";
      link.rel = "noopener";
      tdTime.appendChild(link);

      const tdNote = document.createElement("td");
      const note = document.createElement("input");
      note.className = "note-input";
      note.value = b.label || "";
      note.placeholder = "Add a note\u2026";
      note.addEventListener("change", async () => {
        await STORE.updateBookmark(v.videoId, b.id, { label: note.value });
        toast("Note saved");
      });
      tdNote.appendChild(note);

      const tdActions = document.createElement("td");
      tdActions.className = "td-actions";

      const copyBtn = document.createElement("button");
      copyBtn.className = "btn icon";
      copyBtn.textContent = "Copy URL";
      copyBtn.addEventListener("click", async () => {
        try {
          await navigator.clipboard.writeText(watchUrl(v.videoId, b.time));
          toast("URL copied");
        } catch (e) { toast("Copy failed"); }
      });

      const delBtn = document.createElement("button");
      delBtn.className = "btn icon danger";
      delBtn.textContent = "Delete";
      delBtn.addEventListener("click", async () => {
        await STORE.deleteBookmark(v.videoId, b.id);
        const fresh = await STORE.getVideo(v.videoId);
        renderTimeline(fresh);
        renderBookmarkRows(fresh);
        toast("Bookmark deleted");
      });

      tdActions.appendChild(copyBtn);
      tdActions.appendChild(document.createTextNode(" "));
      tdActions.appendChild(delBtn);

      tr.appendChild(tdNum);
      tr.appendChild(tdTime);
      tr.appendChild(tdNote);
      tr.appendChild(tdActions);
      body.appendChild(tr);
    });
  }

  function backToList() {
    detailView.hidden = true;
    listView.hidden = false;
    currentVideoId = null;
    renderList();
  }

  // ---- toolbar / data ----------------------------------------------------

  document.getElementById("back").addEventListener("click", backToList);

  document.getElementById("clear-video").addEventListener("click", async () => {
    if (!currentVideoId) return;
    if (!confirm("Clear all bookmarks for this video?")) return;
    await STORE.clearBookmarks(currentVideoId);
    const fresh = await STORE.getVideo(currentVideoId);
    renderTimeline(fresh);
    renderBookmarkRows(fresh);
    toast("Bookmarks cleared");
  });

  document.getElementById("delete-video").addEventListener("click", async () => {
    if (!currentVideoId) return;
    if (!confirm("Delete this video record and all its bookmarks?")) return;
    await STORE.deleteVideo(currentVideoId);
    backToList();
    toast("Video deleted");
  });

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
      const data = JSON.parse(await file.text());
      const replace = confirm(
        "Import bookmarks.\n\nOK = Merge with existing data\nCancel = Replace all existing data"
      );
      await STORE.importData(data, replace ? "merge" : "replace");
      renderList();
      toast("Imported");
    } catch (e) {
      alert("Import failed: " + e.message);
    } finally {
      importFile.value = "";
    }
  });

  document.getElementById("settings").addEventListener("click", () => {
    if (api.runtime.openOptionsPage) api.runtime.openOptionsPage();
    else api.runtime.sendMessage({ type: "PC_OPEN_OPTIONS" });
  });

  searchEl.addEventListener("input", renderList);
  sortEl.addEventListener("change", renderList);

  renderList();
})();
