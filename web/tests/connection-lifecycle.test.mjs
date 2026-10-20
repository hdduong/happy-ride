import assert from "node:assert/strict";
import test from "node:test";
import { openConnection } from "../features/rides/connectionLifecycle.ts";

const flush = () => new Promise((resolve) => setImmediate(resolve));

for (const state of ["Connecting", "Connected", "Reconnecting"]) {
  test(`disposal stops a ${state} connection and suppresses callbacks`, async () => {
    let resolveStart;
    let stops = 0;
    let delivered = 0;
    let deliver;
    const starting = state === "Connecting"
      ? new Promise((resolve) => { resolveStart = resolve; })
      : Promise.resolve();
    const close = openConnection((isActive) => {
      deliver = () => { if (isActive()) delivered += 1; };
      return { connection: {
        start: () => starting,
        stop: async () => { stops += 1; },
      } };
    }, () => assert.fail("Unexpected connection error"));
    await flush();
    deliver();
    assert.equal(delivered, 1);
    close();
    assert.equal(stops, 1);
    deliver();
    assert.equal(delivered, 1);
    if (resolveStart) {
      resolveStart();
      await flush();
      assert.equal(stops, 2, "a start that completes after disposal is stopped again");
    }
  });
}

test("a rejected start after disposal is handled without reporting a live connection failure", async () => {
  let rejectStart;
  const errors = [];
  const close = openConnection(() => ({ connection: {
    start: () => new Promise((_, reject) => { rejectStart = reject; }),
    stop: async () => {},
  } }), (error) => errors.push(error));
  close();
  rejectStart(new Error("Stopped during negotiation"));
  await flush();
  assert.deepEqual(errors, []);
});
