import assert from "node:assert/strict";
import test from "node:test";
import { createActivityClient, recordAction } from "../shared/telemetry/activity.ts";
import { mountActivity, getActivityClient } from "../shared/telemetry/browser.ts";
import { createRideSession } from "../features/rides/session.ts";
import { observeRideActivity } from "../features/rides/activity.ts";
import { observeConnectionActivity } from "../features/rides/connectionActivity.ts";
import { openConnection } from "../features/rides/connectionLifecycle.ts";

const flush = () => new Promise((resolve) => setImmediate(resolve));
const appSessionId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const rideId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
const nextRideId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
const ride = (status = "Accepted", id = rideId) => ({
  rideId: id, status, pickup: { lat: 10, lng: 106, address: "Pickup" },
  dropoff: { lat: 11, lng: 107, address: "Drop-off" },
  distanceM: 1000, durationS: 300, fareVnd: 15000, round: 1,
  route: { type: "LineString", coordinates: [[106, 10], [107, 11]] }, driver: null,
});
function harness(send) {
  const events = [];
  const retries = [];
  const client = createActivityClient(appSessionId, {
    id: () => crypto.randomUUID(), now: () => new Date("2026-10-21T04:00:00Z"),
    send: async (event) => { events.push(event); return send ? send(event) : "sent"; },
    schedule: (callback) => { retries.push(callback); return () => {}; },
  });
  return { client, events, retries };
}

for (const [role, terminal] of [["rider", "Completed"], ["rider", "Cancelled"], ["rider", "NoDriver"], ["driver", "DroppedOff"], ["driver", "Cancelled"]]) {
  test(`${role} ends ride context at ${terminal} without tagging the next session activity`, async () => {
    const { client, events } = harness();
    const session = createRideSession(role);
    const stop = observeRideActivity(session, role, client);
    session.receiveRide(ride());
    session.receivePoll(session.pollRevision(), { ride: ride(), offers: [] });
    session.receiveLocation({ rideId, driverId: "driver", lat: 10, lng: 106 });
    session.receiveRide(ride(terminal));
    client.track("ConnectionReconnected");
    session.receiveRide(ride("Searching", nextRideId));
    await flush();
    assert.deepEqual(events.map((e) => [e.eventName, e.rideId]), [
      ["RideContextStarted", rideId], ["RideContextEnded", rideId],
      ["ConnectionReconnected", undefined], ["RideContextStarted", nextRideId],
    ]);
    stop();
    session.receiveRide(ride("Completed", nextRideId));
    await flush();
    assert.equal(events.length, 4, "disposed observers do not log old persona updates");
  });
}

test("rider keeps ride context through drop-off until confirmation", async () => {
  const { client, events } = harness();
  const session = createRideSession("rider");
  const stop = observeRideActivity(session, "rider", client);
  session.receiveRide(ride("DroppedOff"));
  client.track("AppBackgrounded");
  await flush();
  assert.equal(events[1].rideId, rideId);
  stop();
});

test("only displayed offers are logged once despite repeated polls and locations", async () => {
  const { client, events } = harness();
  const session = createRideSession("driver");
  const stop = observeRideActivity(session, "driver", client);
  const first = { ...ride(), offerId: crypto.randomUUID(), expiresAt: "2026-10-21T04:01:00Z", pickupDistanceM: 10 };
  const second = { ...first, offerId: crypto.randomUUID(), expiresAt: "2026-10-21T04:02:00Z" };
  session.receivePoll(session.pollRevision(), { ride: null, offers: [first, second] });
  session.receivePoll(session.pollRevision(), { ride: null, offers: [first, second] });
  session.closeOffer({ offerId: first.offerId, reason: "Declined" });
  await flush();
  assert.deepEqual(events.map((e) => [e.eventName, e.offerId, e.rideId]), [
    ["OfferDisplayed", first.offerId, rideId], ["OfferDisplayed", second.offerId, rideId],
  ]);
  stop();
});

test("offline retries retain event GUID and captured ride instead of using the next ride", async () => {
  let online = false;
  const { client, events, retries } = harness(() => online ? "sent" : "retry");
  client.track("ActionRequested", { action: "Start", rideId });
  await flush();
  client.setRideContext(nextRideId);
  online = true;
  retries.shift()();
  await flush();
  assert.equal(events[0].activityId, events[1].activityId);
  assert.equal(events[1].rideId, rideId);
  assert.equal(events[2].rideId, nextRideId);
});

test("telemetry failure never delays or changes command results and retries are bounded", async () => {
  const { client, events, retries } = harness(() => { throw new Error("offline"); });
  let commandHeader;
  const result = await recordAction(client, "Start", { rideId }, async (headers) => {
    commandHeader = headers;
    return "success";
  });
  assert.equal(result, "success");
  await flush();
  retries.shift()(); await flush();
  retries.shift()(); await flush();
  assert.equal(events.filter((e) => e.eventName === "ActionRequested").length, 3);
  assert.equal(events[0].activityId, commandHeader.activityId);
  assert.equal(events.find((e) => e.eventName === "ActionSucceeded").activityId, commandHeader.activityId);
  client.close();
  await flush();
});

test("concurrent action attempts keep independent GUIDs and ride IDs across async completion", async () => {
  const { client, events } = harness();
  let finish;
  const first = recordAction(client, "Start", { rideId }, () => new Promise((resolve) => { finish = resolve; }));
  await recordAction(client, "Cancel", { rideId: nextRideId }, async () => undefined);
  finish();
  await first;
  await flush();
  const firstEvents = events.filter((e) => e.rideId === rideId);
  const secondEvents = events.filter((e) => e.rideId === nextRideId);
  assert.equal(firstEvents[0].activityId, firstEvents[1].activityId);
  assert.equal(secondEvents[0].activityId, secondEvents[1].activityId);
  assert.notEqual(firstEvents[0].activityId, secondEvents[0].activityId);
});

test("an unavailable telemetry endpoint cannot grow the memory queue indefinitely", async () => {
  let release;
  let blocked = true;
  const { client, events } = harness(() => blocked
    ? new Promise((resolve) => { release = resolve; }) : "sent");
  for (let i = 0; i < 1000; i += 1) client.track("AppResumed");
  assert.equal(events.length, 1, "only one send is in flight");
  blocked = false;
  release("sent");
  await flush();
  assert.equal(events.length, 64);
  client.track("AppBackgrounded");
  await flush();
  assert.equal(events.length, 65, "logging recovers after the bounded backlog drains");
});

test("booking joins its result to its request by GUID and failed actions omit error text", async () => {
  const { client, events } = harness();
  await recordAction(client, "Book", {}, async () => rideId, (id) => id);
  const failure = new Error("private address or token must not be logged");
  await assert.rejects(recordAction(client, "Cancel", { rideId }, async () => { throw failure; }), failure);
  await flush();
  assert.equal(events[0].rideId, undefined);
  assert.equal(events[1].rideId, rideId);
  assert.equal(events[0].activityId, events[1].activityId);
  assert.equal(events.at(-1).eventName, "ActionFailed");
  assert.ok(!JSON.stringify(events).includes(failure.message));
});

test("connection reconnects retain ride/session, update connection ID, and ignore callbacks after disposal", async () => {
  const { client, events } = harness();
  client.setRideContext(rideId);
  const callbacks = {};
  let lifecycle;
  const close = openConnection((isActive) => {
    const connection = {
      connectionId: "connection-1", start: async () => {}, stop: async () => {},
      onreconnecting: (callback) => { callbacks.reconnecting = callback; },
      onreconnected: (callback) => { callbacks.reconnected = callback; },
      onclose: (callback) => { callbacks.closed = callback; },
    };
    lifecycle = observeConnectionActivity(connection, isActive, client);
    return { connection };
  }, () => lifecycle.failed(), () => lifecycle.connected());
  await flush();
  callbacks.reconnecting(); callbacks.reconnected("connection-2"); callbacks.closed();
  await flush();
  close();
  callbacks.reconnecting(); callbacks.reconnected("stale"); callbacks.closed();
  await flush();
  assert.deepEqual(events.slice(1).map((e) => [e.eventName, e.connectionId]), [
    ["ConnectionConnected", "connection-1"], ["ConnectionReconnecting", "connection-1"],
    ["ConnectionReconnected", "connection-2"], ["ConnectionClosed", "connection-2"],
  ]);
  assert.ok(events.every((e) => e.rideId === rideId && e.appSessionId === appSessionId));
});

test("initial connection failure is logged once without exception details", async () => {
  const { client, events } = harness();
  let lifecycle;
  const close = openConnection((isActive) => {
    const connection = {
      connectionId: null, start: async () => { throw new Error("private URL"); }, stop: async () => {},
      onreconnecting: () => {}, onreconnected: () => {}, onclose: () => {},
    };
    lifecycle = observeConnectionActivity(connection, isActive, client);
    return { connection };
  }, () => lifecycle.failed(), () => lifecycle.connected());
  await flush();
  assert.deepEqual(events.map((e) => e.eventName), ["ConnectionFailed"]);
  close();
});

test("browser lifecycle survives StrictMode and bfcache and rotates sessions on persona changes", async () => {
  const originals = Object.getOwnPropertyDescriptors(globalThis);
  const document = Object.assign(new EventTarget(), { visibilityState: "visible" });
  const window = new EventTarget();
  const stored = new Map();
  const events = [];
  Object.assign(globalThis, { document, window,
    sessionStorage: {
      getItem: (key) => stored.get(key) ?? null,
      setItem: (key, value) => stored.set(key, value),
      removeItem: (key) => stored.delete(key),
    },
    fetch: async (url, options) => {
      if (String(url).endsWith("/backend/session")) return { ok: true, status: 204 };
      events.push({ ...JSON.parse(options.body), userId: options.headers["X-User-Id"] });
      return { ok: true, status: 204 };
    },
  });
  try {
    const first = mountActivity("rider");
    first.dispose();
    const remount = mountActivity("rider");
    await flush();
    assert.equal(first.client, remount.client);
    assert.equal(events.filter((e) => e.eventName === "AppOpened").length, 1);
    remount.client.setRideContext(rideId);
    document.visibilityState = "hidden";
    document.dispatchEvent(new Event("visibilitychange"));
    window.dispatchEvent(new Event("pagehide"));
    document.visibilityState = "visible";
    window.dispatchEvent(Object.assign(new Event("pageshow"), { persisted: true }));
    await flush();
    assert.deepEqual(events.slice(-3).map((e) => e.eventName), ["AppBackgrounded", "AppClosed", "AppResumed"]);
    assert.ok(events.slice(-3).every((e) => e.rideId === rideId));
    remount.dispose();
    const driver = mountActivity("driver");
    await flush();
    assert.notEqual(driver.client.appSessionId, remount.client.appSessionId);
    assert.equal(getActivityClient("rider"), undefined);
    assert.equal(driver.client.correlation().rideId, undefined);
    driver.dispose();
    await flush();
    const returningRider = mountActivity("rider");
    assert.notEqual(returningRider.client.appSessionId, remount.client.appSessionId);
    returningRider.dispose();
    await flush();
  } finally {
    for (const name of ["document", "window", "sessionStorage", "fetch"]) {
      if (originals[name]) Object.defineProperty(globalThis, name, originals[name]);
      else delete globalThis[name];
    }
  }
});
