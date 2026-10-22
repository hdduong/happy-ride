import assert from "node:assert/strict";
import test from "node:test";
import { bindMapConfigRefresh } from "../features/maps/refresh.ts";

test("an open map reloads when the hub reconnects or the provider changes", () => {
  const events = [];
  const window = new EventTarget();
  window.addEventListener("happy-ride-map-config", () => events.push("refresh"));
  const previous = globalThis.window;
  globalThis.window = window;
  const handlers = {};
  try {
    bindMapConfigRefresh({
      on(event, callback) { handlers[event] = callback; },
      onreconnected(callback) { handlers.reconnected = callback; },
    }, () => true);
    handlers.MapConfigChanged();
    handlers.reconnected();
    bindMapConfigRefresh({
      on(event, callback) { handlers[event] = callback; },
      onreconnected(callback) { handlers.reconnected = callback; },
    }, () => false);
    handlers.MapConfigChanged();
    handlers.reconnected();
  } finally {
    if (previous === undefined) delete globalThis.window;
    else globalThis.window = previous;
  }
  assert.deepEqual(events, ["refresh", "refresh"]);
});
