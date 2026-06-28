// Loopback WebSocket bridge to the optional local companion app.
//
// The companion app registers system-wide global hotkeys and forwards them as
// `{ type: "command", action }` messages over ws://127.0.0.1:<port>. This
// module is a *client*: it only ever tries to connect and silently retries on
// failure, so the extension keeps working perfectly when the app is absent.
//
// Exposes `window.PC_BRIDGE`.

(function () {
  "use strict";

  let ws = null;
  let enabled = false;
  let port = 8423;
  let connected = false;
  let retryTimer = null;
  let backoff = 1000;
  const MAX_BACKOFF = 15000;

  let onCommand = null; // (action) => Promise<{ok, info}>
  let shortcutsProvider = null; // () => Promise<map> | map ; current shortcuts

  function url() {
    return "ws://127.0.0.1:" + port + "/";
  }

  // Push the current shortcut map to the app so it can register the same keys
  // as system-wide global hotkeys. Best-effort; ignored if disconnected.
  async function pushShortcuts() {
    if (!shortcutsProvider) return;
    let map = null;
    try {
      map = await shortcutsProvider();
    } catch (e) {
      return;
    }
    if (map && typeof map === "object") {
      safeSend({ type: "shortcuts", shortcuts: map });
    }
  }

  function scheduleReconnect() {
    if (!enabled) return;
    clearTimeout(retryTimer);
    retryTimer = setTimeout(connect, backoff);
    backoff = Math.min(Math.round(backoff * 1.5), MAX_BACKOFF);
  }

  function connect() {
    if (!enabled) return;
    closeSocket();
    let sock;
    try {
      sock = new WebSocket(url());
    } catch (e) {
      scheduleReconnect();
      return;
    }
    ws = sock;
    sock.onopen = function () {
      if (ws !== sock) return;
      connected = true;
      backoff = 1000;
      safeSend({ type: "hello", app: "PlayControl-ext" });
      pushShortcuts();
    };
    sock.onmessage = function (ev) {
      if (ws !== sock) return;
      handleMessage(ev.data);
    };
    sock.onclose = function () {
      if (ws === sock) {
        connected = false;
        ws = null;
        scheduleReconnect();
      }
    };
    sock.onerror = function () {
      // onclose follows and handles reconnect.
    };
  }

  function safeSend(obj) {
    if (ws && ws.readyState === 1) {
      try {
        ws.send(JSON.stringify(obj));
      } catch (e) {
        /* ignore */
      }
    }
  }

  async function handleMessage(data) {
    let msg;
    try {
      msg = JSON.parse(data);
    } catch (e) {
      return;
    }
    if (!msg || msg.type !== "command" || typeof msg.action !== "string") return;
    let result = { ok: false };
    if (onCommand) {
      try {
        result = (await onCommand(msg.action)) || { ok: false };
      } catch (e) {
        result = { ok: false, error: String(e && e.message) };
      }
    }
    // Echo a result so the app can render accurate feedback (and the request
    // id, if any, for correlation).
    safeSend({
      type: "result",
      id: msg.id,
      action: msg.action,
      ok: !!result.ok,
      info: result.info || null,
    });
  }

  function closeSocket() {
    if (ws) {
      try {
        ws.onopen = ws.onmessage = ws.onclose = ws.onerror = null;
        ws.close();
      } catch (e) {
        /* ignore */
      }
      ws = null;
    }
  }

  function enable(p) {
    if (typeof p === "number" && p > 0) port = p;
    enabled = true;
    backoff = 1000;
    clearTimeout(retryTimer);
    connect();
  }

  function disable() {
    enabled = false;
    connected = false;
    clearTimeout(retryTimer);
    closeSocket();
  }

  // Apply a desired state from settings without churning a healthy connection.
  function configure(nextEnabled, nextPort) {
    const portChanged = typeof nextPort === "number" && nextPort > 0 && nextPort !== port;
    if (nextEnabled) {
      if (!enabled || portChanged) {
        if (typeof nextPort === "number" && nextPort > 0) port = nextPort;
        enable(port);
      }
    } else if (enabled) {
      disable();
    }
  }

  function status() {
    return { enabled: enabled, connected: connected, port: port };
  }

  window.PC_BRIDGE = {
    configure: configure,
    enable: enable,
    disable: disable,
    status: status,
    setCommandHandler: function (cb) {
      onCommand = cb;
    },
    // Provider returning the current shortcut map; called on each (re)connect.
    setShortcutsProvider: function (cb) {
      shortcutsProvider = cb;
    },
    // Push the latest shortcuts now (e.g. after a settings change).
    pushShortcuts: pushShortcuts,
  };
})();
