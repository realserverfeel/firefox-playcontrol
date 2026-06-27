// Storage layer for PlayControl. Wraps browser.storage.local and exposes a
// small CRUD API for settings and per-video bookmarks. Exposes `window.PC_STORE`.

(function () {
  "use strict";

  const D = window.PC_DEFAULTS;
  const api = typeof browser !== "undefined" ? browser : chrome;

  function deepMerge(base, override) {
    if (Array.isArray(base) || typeof base !== "object" || base === null) {
      return override === undefined ? base : override;
    }
    const out = { ...base };
    if (override && typeof override === "object") {
      for (const key of Object.keys(override)) {
        out[key] = deepMerge(base[key], override[key]);
      }
    }
    return out;
  }

  async function getSettings() {
    const res = await api.storage.local.get(D.STORAGE_KEYS.settings);
    return deepMerge(D.DEFAULT_SETTINGS, res[D.STORAGE_KEYS.settings] || {});
  }

  async function setSettings(settings) {
    await api.storage.local.set({ [D.STORAGE_KEYS.settings]: settings });
    return settings;
  }

  async function getAllVideos() {
    const res = await api.storage.local.get(D.STORAGE_KEYS.videos);
    return res[D.STORAGE_KEYS.videos] || {};
  }

  async function getVideo(videoId) {
    const videos = await getAllVideos();
    return videos[videoId] || null;
  }

  async function saveVideo(video) {
    const videos = await getAllVideos();
    videos[video.videoId] = video;
    await api.storage.local.set({ [D.STORAGE_KEYS.videos]: videos });
    return video;
  }

  async function deleteVideo(videoId) {
    const videos = await getAllVideos();
    delete videos[videoId];
    await api.storage.local.set({ [D.STORAGE_KEYS.videos]: videos });
  }

  function uuid() {
    if (typeof crypto !== "undefined" && crypto.randomUUID) {
      return crypto.randomUUID();
    }
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
      const r = (Math.random() * 16) | 0;
      const v = c === "x" ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  // Ensures a video record exists, updating title/url/lastWatched metadata.
  async function touchVideo(meta) {
    const existing = (await getVideo(meta.videoId)) || {
      videoId: meta.videoId,
      title: meta.title || "",
      url: meta.url || "",
      bookmarks: [],
      createdAt: Date.now(),
    };
    if (meta.title) existing.title = meta.title;
    if (meta.url) existing.url = meta.url;
    existing.lastWatched = Date.now();
    await saveVideo(existing);
    return existing;
  }

  async function addBookmark(meta, time, label) {
    const video = await touchVideo(meta);
    const bookmark = {
      id: uuid(),
      time: Math.max(0, Math.round(time * 1000) / 1000),
      label: label || "",
      createdAt: Date.now(),
    };
    video.bookmarks.push(bookmark);
    video.bookmarks.sort((a, b) => a.time - b.time);
    await saveVideo(video);
    return bookmark;
  }

  async function updateBookmark(videoId, bookmarkId, patch) {
    const video = await getVideo(videoId);
    if (!video) return null;
    const bm = video.bookmarks.find((b) => b.id === bookmarkId);
    if (!bm) return null;
    if (patch.label !== undefined) bm.label = patch.label;
    if (patch.time !== undefined) bm.time = Math.max(0, patch.time);
    video.bookmarks.sort((a, b) => a.time - b.time);
    await saveVideo(video);
    return bm;
  }

  async function deleteBookmark(videoId, bookmarkId) {
    const video = await getVideo(videoId);
    if (!video) return;
    video.bookmarks = video.bookmarks.filter((b) => b.id !== bookmarkId);
    await saveVideo(video);
  }

  async function clearBookmarks(videoId) {
    const video = await getVideo(videoId);
    if (!video) return;
    video.bookmarks = [];
    await saveVideo(video);
  }

  async function exportData() {
    const [settings, videos] = await Promise.all([getSettings(), getAllVideos()]);
    return {
      app: "PlayControl",
      schemaVersion: D.SCHEMA_VERSION,
      exportedAt: new Date().toISOString(),
      settings,
      videos,
    };
  }

  // mode: "merge" keeps existing videos and merges bookmarks; "replace" overwrites.
  async function importData(data, mode) {
    if (!data || typeof data !== "object") throw new Error("Invalid data");
    if (data.settings) await setSettings(deepMerge(D.DEFAULT_SETTINGS, data.settings));

    const incoming = data.videos || {};
    if (mode === "replace") {
      await api.storage.local.set({ [D.STORAGE_KEYS.videos]: incoming });
      return;
    }
    const current = await getAllVideos();
    for (const id of Object.keys(incoming)) {
      const inc = incoming[id];
      if (!current[id]) {
        current[id] = inc;
        continue;
      }
      const seen = new Set(current[id].bookmarks.map((b) => b.id));
      for (const bm of inc.bookmarks || []) {
        if (!seen.has(bm.id)) current[id].bookmarks.push(bm);
      }
      current[id].bookmarks.sort((a, b) => a.time - b.time);
      if (inc.title) current[id].title = inc.title;
    }
    await api.storage.local.set({ [D.STORAGE_KEYS.videos]: current });
  }

  async function clearAll() {
    await api.storage.local.remove([D.STORAGE_KEYS.videos]);
  }

  window.PC_STORE = {
    getSettings,
    setSettings,
    getAllVideos,
    getVideo,
    saveVideo,
    deleteVideo,
    touchVideo,
    addBookmark,
    updateBookmark,
    deleteBookmark,
    clearBookmarks,
    exportData,
    importData,
    clearAll,
    uuid,
  };
})();
