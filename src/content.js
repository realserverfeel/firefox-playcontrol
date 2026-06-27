// PlayControl content script. Keyboard-driven control of the page's <video>
// element with a minimal PotPlayer-style on-screen text overlay. Deliberately
// avoids touching YouTube's own DOM beyond a single overlay element so it keeps
// working across YouTube UI redesigns.

(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;
  const KEYS = window.PC_KEYS;
  const STORE = window.PC_STORE;
  const DEFAULTS = window.PC_DEFAULTS;

  let settings = DEFAULTS.DEFAULT_SETTINGS;
  let osdEl = null;
  let osdTimer = null;
  let segmentStopTime = null; // when set, pause once currentTime passes it
  let clearConfirmTimer = null; // pending double-press to confirm clear

  // ---- video discovery ---------------------------------------------------

  function getVideo() {
    // The largest playing <video> is the main one. Fall back to first video.
    const vids = Array.from(document.querySelectorAll("video"));
    if (vids.length === 0) return null;
    let best = null;
    let bestArea = -1;
    for (const v of vids) {
      const area = v.clientWidth * v.clientHeight;
      if (area > bestArea && v.readyState > 0) {
        best = v;
        bestArea = area;
      }
    }
    return best || vids[0];
  }

  function getVideoId() {
    const url = new URL(location.href);
    const v = url.searchParams.get("v");
    if (v) return v;
    const shorts = location.pathname.match(/\/shorts\/([^/?]+)/);
    if (shorts) return shorts[1];
    const embed = location.pathname.match(/\/embed\/([^/?]+)/);
    if (embed) return embed[1];
    return null;
  }

  function getTitle() {
    const metaTitle = document.querySelector('meta[name="title"]');
    if (metaTitle && metaTitle.content) return metaTitle.content;
    const h1 = document.querySelector("h1.title, h1.ytd-watch-metadata");
    if (h1 && h1.textContent.trim()) return h1.textContent.trim();
    return document.title.replace(/ - YouTube$/, "").trim();
  }

  function getMeta() {
    return { videoId: getVideoId(), title: getTitle(), url: cleanUrl() };
  }

  function cleanUrl(time) {
    const id = getVideoId();
    let base;
    if (location.pathname.startsWith("/shorts/")) {
      base = `https://www.youtube.com/shorts/${id}`;
    } else {
      base = `https://www.youtube.com/watch?v=${id}`;
    }
    if (time != null) {
      const t = Math.floor(time);
      base += (base.includes("?") ? "&" : "?") + `t=${t}s`;
    }
    return base;
  }

  // ---- OSD overlay -------------------------------------------------------

  function ensureOsd() {
    if (osdEl && osdEl.isConnected) return osdEl;
    osdEl = document.createElement("div");
    osdEl.id = "playcontrol-osd";
    osdEl.setAttribute("aria-hidden", "true");
    return osdEl;
  }

  function showOsd(text) {
    if (!settings.osd.enabled) return;
    const el = ensureOsd();
    // Attach to the fullscreen element when present so it shows in fullscreen.
    const host = document.fullscreenElement || document.body;
    if (el.parentElement !== host) host.appendChild(el);

    el.textContent = text;
    el.style.fontSize = settings.osd.fontSize + "px";
    el.style.color = settings.osd.color;
    el.classList.add("playcontrol-osd-visible");

    if (osdTimer) clearTimeout(osdTimer);
    osdTimer = setTimeout(() => {
      el.classList.remove("playcontrol-osd-visible");
    }, settings.osd.durationMs);
  }

  function fmtTime(sec) {
    sec = Math.max(0, Math.floor(sec));
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    const s = sec % 60;
    const pad = (n) => String(n).padStart(2, "0");
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
  }

  // ---- actions -----------------------------------------------------------

  function togglePlay(video) {
    if (video.paused) {
      video.play();
      showOsd("\u25B6 Play");
    } else {
      video.pause();
      showOsd("\u2759\u2759 Pause");
    }
  }

  function seek(video, delta) {
    segmentStopTime = null;
    const t = Math.max(0, Math.min(video.duration || Infinity, video.currentTime + delta));
    video.currentTime = t;
    const sign = delta >= 0 ? "+" : "\u2212";
    showOsd(`${delta >= 0 ? "\u23E9" : "\u23EA"} ${sign}${Math.abs(delta)}s  (${fmtTime(t)})`);
  }

  async function markBookmark(video) {
    const meta = getMeta();
    if (!meta.videoId) {
      showOsd("\u26A0 No video id");
      return;
    }
    try {
      const bm = await STORE.addBookmark(meta, video.currentTime);
      showOsd(`\uD83D\uDCCC Bookmark  (${fmtTime(bm.time)})`);
    } catch (e) {
      showOsd("\u26A0 Storage full");
    }
  }

  async function clearBookmarksAction(video) {
    const meta = getMeta();
    if (!meta.videoId) return;
    const v = await STORE.getVideo(meta.videoId);
    if (!v || v.bookmarks.length === 0) {
      showOsd("\uD83D\uDCCC No bookmarks");
      return;
    }
    // Optional two-step confirmation: first press arms, second press clears.
    if (settings.confirmClearBookmarks && !clearConfirmTimer) {
      showOsd(`\u26A0 Press again to clear ${v.bookmarks.length} bookmark(s)`);
      clearConfirmTimer = setTimeout(() => {
        clearConfirmTimer = null;
      }, 4000);
      return;
    }
    if (clearConfirmTimer) {
      clearTimeout(clearConfirmTimer);
      clearConfirmTimer = null;
    }
    await STORE.clearBookmarks(meta.videoId);
    showOsd("\uD83D\uDDD1 All bookmarks cleared");
  }

  async function jumpBookmark(video, dir) {
    const meta = getMeta();
    if (!meta.videoId) return;
    const v = await STORE.getVideo(meta.videoId);
    const bms = (v && v.bookmarks) || [];
    if (bms.length === 0) {
      showOsd("\uD83D\uDCCC No bookmarks");
      return;
    }
    const now = video.currentTime;
    const eps = 0.4;
    let target = null;
    if (dir < 0) {
      for (let i = bms.length - 1; i >= 0; i--) {
        if (bms[i].time < now - eps) { target = bms[i]; break; }
      }
    } else {
      for (let i = 0; i < bms.length; i++) {
        if (bms[i].time > now + eps) { target = bms[i]; break; }
      }
    }
    if (!target) {
      showOsd(dir < 0 ? "\u23EE First bookmark" : "\u23ED Last bookmark");
      return;
    }
    segmentStopTime = null;
    video.currentTime = target.time;
    const label = target.label ? `  ${target.label}` : "";
    showOsd(`${dir < 0 ? "\u23EE" : "\u23ED"} ${fmtTime(target.time)}${label}`);
  }

  // Jump to the previous bookmark (or start) and play until the next bookmark.
  async function playSegment(video) {
    const meta = getMeta();
    if (!meta.videoId) return;
    const v = await STORE.getVideo(meta.videoId);
    const bms = (v && v.bookmarks) || [];
    if (bms.length === 0) {
      showOsd("\uD83D\uDCCC No bookmarks");
      return;
    }
    const now = video.currentTime;
    const eps = 0.4;
    let start = 0;
    let end = null;
    for (let i = bms.length - 1; i >= 0; i--) {
      if (bms[i].time <= now + eps) { start = bms[i].time; break; }
    }
    for (let i = 0; i < bms.length; i++) {
      if (bms[i].time > start + eps) { end = bms[i].time; break; }
    }
    video.currentTime = start;
    segmentStopTime = end; // null means play to end of video
    video.play();
    showOsd(`\u25B6 Segment ${fmtTime(start)} \u2192 ${end != null ? fmtTime(end) : "end"}`);
  }

  async function copyUrl(video) {
    const url = cleanUrl(video.currentTime);
    try {
      await navigator.clipboard.writeText(url);
      showOsd(`\uD83D\uDCCB Copied  (${fmtTime(video.currentTime)})`);
    } catch (e) {
      // Fallback for clipboard restrictions.
      const ta = document.createElement("textarea");
      ta.value = url;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); showOsd("\uD83D\uDCCB Copied"); }
      catch (_) { showOsd("\u26A0 Copy failed"); }
      ta.remove();
    }
  }

  const HANDLERS = {
    togglePlay: (v) => togglePlay(v),
    seekBack: (v) => seek(v, -settings.seekSeconds),
    seekForward: (v) => seek(v, settings.seekSeconds),
    markBookmark: (v) => markBookmark(v),
    prevBookmark: (v) => jumpBookmark(v, -1),
    nextBookmark: (v) => jumpBookmark(v, 1),
    playSegment: (v) => playSegment(v),
    copyUrl: (v) => copyUrl(v),
    clearBookmarks: (v) => clearBookmarksAction(v),
  };

  // Best-effort: wake YouTube's native controls / seek bar so they flash on
  // shortcut use. Uses a YouTube-specific selector but degrades silently.
  function wakeNativeControls(video) {
    const player =
      (video && (video.closest(".html5-video-player") || video.parentElement)) ||
      document.querySelector("#movie_player, .html5-video-player");
    if (!player) return;
    const rect = player.getBoundingClientRect();
    const evt = new MouseEvent("mousemove", {
      bubbles: true,
      cancelable: true,
      view: window,
      clientX: rect.left + rect.width / 2,
      clientY: rect.top + rect.height - 10,
    });
    player.dispatchEvent(evt);
  }

  function runAction(action) {
    const video = getVideo();
    if (!video) {
      showOsd("\u26A0 No video");
      return;
    }
    const fn = HANDLERS[action];
    if (fn) fn(video);
    if (settings.activateNativeControls) wakeNativeControls(video);
  }

  // ---- key handling ------------------------------------------------------

  function isTypingTarget(el) {
    if (!el) return false;
    const tag = el.tagName;
    return (
      el.isContentEditable ||
      tag === "INPUT" ||
      tag === "TEXTAREA" ||
      tag === "SELECT"
    );
  }

  function onKeyDown(e) {
    if (isTypingTarget(e.target)) return;
    const action = KEYS.matchAction(e, settings.shortcuts);
    if (!action) return;
    e.preventDefault();
    e.stopPropagation();
    runAction(action);
  }

  // ---- segment auto-stop -------------------------------------------------

  function onTimeUpdate(e) {
    if (segmentStopTime == null) return;
    if (e.target.currentTime >= segmentStopTime) {
      e.target.pause();
      segmentStopTime = null;
      showOsd("\u2759\u2759 Segment end");
    }
  }

  function attachVideoListeners() {
    const v = getVideo();
    if (v && !v.__pcBound) {
      v.__pcBound = true;
      v.addEventListener("timeupdate", onTimeUpdate);
    }
  }

  // ---- settings + lifecycle ---------------------------------------------

  async function loadSettings() {
    settings = await STORE.getSettings();
  }

  api.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && changes[DEFAULTS.STORAGE_KEYS.settings]) {
      loadSettings();
    }
  });

  // Respond to popup/background queries and remote commands.
  api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || !msg.type) return;
    if (msg.type === "PC_GET_STATE") {
      const video = getVideo();
      const meta = getMeta();
      STORE.getVideo(meta.videoId).then((v) => {
        sendResponse({
          hasVideo: !!video,
          paused: video ? video.paused : true,
          currentTime: video ? video.currentTime : 0,
          duration: video ? video.duration : 0,
          videoId: meta.videoId,
          title: meta.title,
          bookmarkCount: v ? v.bookmarks.length : 0,
        });
      });
      return true; // async response
    }
    if (msg.type === "PC_RUN_ACTION" && msg.action) {
      runAction(msg.action);
      sendResponse({ ok: true });
      return false;
    }
    if (msg.type === "PC_SEEK_TO" && typeof msg.time === "number") {
      const video = getVideo();
      if (video) {
        segmentStopTime = null;
        video.currentTime = msg.time;
        video.play();
      }
      sendResponse({ ok: !!video });
      return false;
    }
  });

  // Record that this video was watched (for the manager's "recent" list).
  let touchTimer = null;
  function scheduleTouch() {
    if (touchTimer) clearTimeout(touchTimer);
    touchTimer = setTimeout(() => {
      const meta = getMeta();
      if (meta.videoId) STORE.getVideo(meta.videoId).then((v) => {
        if (v) STORE.touchVideo(meta); // only bump if already tracked
      });
    }, 2000);
  }

  function init() {
    document.addEventListener("keydown", onKeyDown, true);
    attachVideoListeners();
    scheduleTouch();
    // YouTube is an SPA; re-bind on navigation and DOM changes.
    const mo = new MutationObserver(() => {
      attachVideoListeners();
    });
    mo.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener("yt-navigate-finish", scheduleTouch);
  }

  loadSettings().then(init);
})();
