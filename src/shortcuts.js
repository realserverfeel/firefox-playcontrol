// Keyboard shortcut helpers shared by the content script and options page.
// Combos are serialized as modifier+code strings, e.g. "Home", "Shift+End",
// "Ctrl+Alt+KeyB". Exposes `window.PC_KEYS`.

(function () {
  "use strict";

  const MODIFIER_CODES = new Set([
    "ShiftLeft", "ShiftRight",
    "ControlLeft", "ControlRight",
    "AltLeft", "AltRight",
    "MetaLeft", "MetaRight",
  ]);

  // Build a combo string from a KeyboardEvent.
  function comboFromEvent(e) {
    if (MODIFIER_CODES.has(e.code)) return null; // ignore lone modifier presses
    const parts = [];
    if (e.ctrlKey) parts.push("Ctrl");
    if (e.altKey) parts.push("Alt");
    if (e.shiftKey) parts.push("Shift");
    if (e.metaKey) parts.push("Meta");
    parts.push(e.code);
    return parts.join("+");
  }

  // Friendly display for a code, e.g. "KeyB" -> "B", "ArrowLeft" -> "\u2190".
  function prettyCode(code) {
    if (!code) return "";
    const map = {
      PageUp: "Page Up",
      PageDown: "Page Down",
      ArrowUp: "\u2191",
      ArrowDown: "\u2193",
      ArrowLeft: "\u2190",
      ArrowRight: "\u2192",
      Escape: "Esc",
      Delete: "Delete",
      Insert: "Insert",
      Home: "Home",
      End: "End",
      Space: "Space",
    };
    if (map[code]) return map[code];
    if (code.startsWith("Key")) return code.slice(3);
    if (code.startsWith("Digit")) return code.slice(5);
    if (code.startsWith("Numpad")) return "Num " + code.slice(6);
    return code;
  }

  function prettyCombo(combo) {
    if (!combo) return "(unset)";
    const parts = combo.split("+");
    const code = parts.pop();
    return [...parts, prettyCode(code)].join(" + ");
  }

  // Find which action a keyboard event maps to, given a shortcuts map.
  function matchAction(e, shortcuts) {
    const combo = comboFromEvent(e);
    if (!combo) return null;
    for (const action of Object.keys(shortcuts)) {
      if (shortcuts[action] && shortcuts[action] === combo) return action;
    }
    return null;
  }

  window.PC_KEYS = { comboFromEvent, prettyCode, prettyCombo, matchAction };
})();
