// Background script. Currently minimal: opens the manager page and relays
// commands if needed. Kept small so future global-hotkey/WebSocket work can
// hook in here.

(function () {
  "use strict";

  const api = typeof browser !== "undefined" ? browser : chrome;

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
  });
})();
